using EasyStock.Api.Configuration;
using EasyStock.Api.Services.Faturacao;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Faturas.EmitirFatura;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Postgre.Concurrency;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EasyStock.Api.BackgroundServices;

public sealed class CobrancaAssinaturaJob(
    IServiceProvider serviceProvider,
    IOptions<BackgroundJobOptions> options,
    ILogger<CobrancaAssinaturaJob> logger) : BackgroundService
{
    private readonly BackgroundJobOptions _options = options.Value;

    private async Task RunWithAdvisoryLockAsync(long key, Func<CancellationToken, Task> action, CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetService<EasyStockDbContext>();
        if (db is null)
        {
            // Sem DbContext: só ocorre em DEV com provider não-relacional ou DI mal configurada.
            // Roda sem lock — aceitável em dev (réplica única), MAS em multi-pod prod isso causa
            // cobrança duplicada. Log em Warning para alertar caso isso vaze pra prod.
            logger.LogWarning("CobrancaAssinaturaJob: DbContext indisponível — executando SEM advisory lock. NÃO usar em multi-pod.");
            await action(ct);
            return;
        }

        // pg_try_advisory_lock é exclusivo do PostgreSQL. Se outro provider (SQLite em dev),
        // pulamos com warning para deixar explícito o risco em ambientes não-PG.
        if (!db.Database.IsNpgsql())
        {
            logger.LogWarning("CobrancaAssinaturaJob: provider {Provider} não suporta advisory lock — executando SEM lock.", db.Database.ProviderName);
            await action(ct);
            return;
        }

        // Tenta adquirir lock — se outra réplica já tem, sai sem rodar.
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            using var cmd = db.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = "SELECT pg_try_advisory_lock(@key)";
            var p = cmd.CreateParameter(); p.ParameterName = "key"; p.Value = key; cmd.Parameters.Add(p);
            var got = (bool)(await cmd.ExecuteScalarAsync(ct) ?? false);
            if (!got)
            {
                logger.LogInformation("CobrancaAssinaturaJob: outra réplica está executando — pulando esta rodada.");
                return;
            }

            try { await action(ct); }
            finally
            {
                using var unlock = db.Database.GetDbConnection().CreateCommand();
                unlock.CommandText = "SELECT pg_advisory_unlock(@key)";
                var pu = unlock.CreateParameter(); pu.ParameterName = "key"; pu.Value = key; unlock.Parameters.Add(pu);
                await unlock.ExecuteScalarAsync(ct);
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Job de cobrança de assinatura iniciado");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;
                var nextRun = now.Date.AddDays(1).AddHours(8); // 08:00 UTC próximo dia
                if (now.Hour < 8)
                    nextRun = now.Date.AddHours(8);

                var delay = nextRun - now;
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, stoppingToken);

                // Distributed lock via Postgres advisory lock — garante que
                // apenas UMA instância (de N réplicas Cloud Run) execute o
                // job no mesmo dia. Sem isso, múltiplas réplicas geram
                // cobranças Pix duplicadas. Lock é liberado ao fim da
                // transação. Key arbitrária estável: hash de "easystock-cobranca-job".
                await RunWithAdvisoryLockAsync(LockKeys.CobrancaAssinatura, async ct =>
                {
                    await ProcessarCobrancasAsync(ct);
                    await SuspenderVencidasAsync(ct);
                    await DunningAsync(ct);
                    await CancelarSuspensasAntigasAsync(ct);
                }, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro no job de cobrança de assinatura");
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
        }
    }

    private async Task ProcessarCobrancasAsync(CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var assinaturaRepo = scope.ServiceProvider.GetRequiredService<IAssinaturaEmpresaRepository>();
        var cobrancaRepo = scope.ServiceProvider.GetRequiredService<ICobrancaAssinaturaRepository>();
        var pixService = scope.ServiceProvider.GetRequiredService<IEfiPixService>();
        var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
        var notificador = scope.ServiceProvider.GetRequiredService<INotificadorService>();
        var usuarioRepo = scope.ServiceProvider.GetRequiredService<IUsuarioRepository>();
        var empresaRepo = scope.ServiceProvider.GetRequiredService<IEmpresaRepository>();
        var emitirFaturaUseCase = scope.ServiceProvider.GetRequiredService<EmitirFaturaUseCase>();
        var faturaFactory = scope.ServiceProvider.GetRequiredService<FaturaSaasFactory>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var vencendo = await assinaturaRepo.GetAtivasVencendoEmAsync(3, ct);

        foreach (var assinatura in vencendo)
        {
            try
            {
                if (await cobrancaRepo.ExistePendenteAsync(assinatura.EmpresaId))
                    continue;

                var plano = assinatura.Plano;
                var valor = plano?.PrecoMensal ?? 0m;
                if (valor <= 0)
                {
                    // Plano grátis: apenas estende vigência, sem gerar Pix.
                    assinatura.DataFim = DateTime.UtcNow.AddDays(30);
                    await assinaturaRepo.UpdateAsync(assinatura);
                    await unitOfWork.CommitAsync();
                    continue;
                }

                var txid = Guid.NewGuid().ToString("N")[..35];
                var descricao = $"Assinatura EasyStock — {plano?.Nome ?? "Plano"}";

                // F5 — Convivencia: emite Fatura junto com a Cobranca, para que
                // o cliente veja o documento no portal e possa baixar PDF. Idempotente
                // por (Origem=Assinatura, OrigemRefId=assinatura.Id) — se ja existir
                // fatura ativa para esta vigencia, reutiliza. Erros aqui NAO bloqueiam
                // a criacao da Cobranca (fatura e convivencia, nao critica para SaaS).
                Guid? faturaIdParaCobranca = null;
                try
                {
                    var empresa = await empresaRepo.GetByIdAsync(assinatura.EmpresaId);
                    if (empresa is not null && plano is not null)
                    {
                        var faturaCmd = faturaFactory.BuildParaAssinatura(
                            assinatura, plano, empresa,
                            dataEmissao: DateTime.UtcNow,
                            dataVencimento: DateTime.UtcNow.AddDays(7));
                        var faturaResult = await emitirFaturaUseCase.ExecuteAsync(faturaCmd, ct);
                        faturaIdParaCobranca = faturaResult.FaturaId;
                        logger.LogInformation(
                            "Fatura emitida junto com cobranca. EmpresaId={EmpresaId} FaturaId={FaturaId} Numero={Numero}",
                            assinatura.EmpresaId, faturaResult.FaturaId, faturaResult.Numero);
                    }
                }
                catch (Exception faturaEx)
                {
                    logger.LogWarning(faturaEx,
                        "Falha ao emitir Fatura para empresa {EmpresaId} — seguindo sem fatura linkada.",
                        assinatura.EmpresaId);
                }

                // 1) Persiste cobrança como Pendente ANTES de chamar Efí.
                //    Se Efí responde 200 mas commit local falha, na próxima
                //    rodada o ExistePendenteAsync evita gerar nova cobrança.
                //    Se Efí falha após persist, marcamos como Falhada e seguimos.
                var cobranca = CobrancaAssinatura.Criar(
                    assinatura.EmpresaId,
                    assinatura.Id,
                    txid,
                    valor,
                    pixCopiaCola: string.Empty,
                    qrCodeBase64: string.Empty,
                    expiracaoEm: DateTime.UtcNow.AddDays(1));
                cobranca.FaturaId = faturaIdParaCobranca;

                await cobrancaRepo.AddAsync(cobranca);
                await unitOfWork.CommitAsync();

                EfiCobrancaResult pixResult;
                try
                {
                    pixResult = await pixService.CriarCobrancaAsync(txid, valor, descricao, ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Falha ao criar cobrança Pix para empresa {EmpresaId} — marcando como falhada", assinatura.EmpresaId);
                    cobranca.MarcarComoFalhada();
                    await cobrancaRepo.UpdateAsync(cobranca);
                    await unitOfWork.CommitAsync();
                    continue;
                }

                cobranca.AtualizarDadosPix(pixResult.PixCopiaCola, pixResult.QrCodeBase64, pixResult.ExpiracaoEm);
                await cobrancaRepo.UpdateAsync(cobranca);
                await unitOfWork.CommitAsync();

                await EnviarEmailCobrancaAsync(emailService, notificador, usuarioRepo, assinatura, cobranca, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro ao processar cobrança para empresa {EmpresaId}", assinatura.EmpresaId);
            }
        }
    }

    private async Task SuspenderVencidasAsync(CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var assinaturaRepo = scope.ServiceProvider.GetRequiredService<IAssinaturaEmpresaRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var vencidas = await assinaturaRepo.GetAtivasVencidasAsync(ct);

        foreach (var assinatura in vencidas)
        {
            try
            {
                assinatura.Suspender();
                await assinaturaRepo.UpdateAsync(assinatura);
                logger.LogInformation("Assinatura suspensa por vencimento. EmpresaId: {EmpresaId}", assinatura.EmpresaId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro ao suspender assinatura {Id}", assinatura.Id);
            }
        }

        await unitOfWork.CommitAsync();
    }

    /// <summary>
    /// Dunning: envia lembretes por email para assinaturas suspensas com cobrança pendente.
    /// Escalonamento: D+1, D+3, D+7 a partir de SuspensaEm. Máx 3 lembretes.
    /// </summary>
    private async Task DunningAsync(CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var cobrancaRepo = scope.ServiceProvider.GetRequiredService<ICobrancaAssinaturaRepository>();
        var usuarioRepo = scope.ServiceProvider.GetRequiredService<IUsuarioRepository>();
        var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
        var notificador = scope.ServiceProvider.GetRequiredService<INotificadorService>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        // Dias a esperar entre lembretes (D+1, D+3, D+7 = gaps de 1, 2, 4 dias)
        var gapsDias = new[] { 1, 2, 4 };

        var candidatas = await cobrancaRepo.GetPendentesParaDunningAsync(ct);

        foreach (var cobranca in candidatas)
        {
            try
            {
                var assinatura = cobranca.Assinatura!;
                var suspensaEm = assinatura.SuspensaEm ?? assinatura.AlteradoEm;
                var tentativa = cobranca.TentativasLembrete; // 0, 1, 2

                if (tentativa >= 3) continue;

                var gapNecessario = tentativa < gapsDias.Length ? gapsDias[tentativa] : int.MaxValue;
                var dataMinima = suspensaEm.AddDays(gapNecessario);

                if (DateTime.UtcNow < dataMinima) continue;

                // Verifica que o último lembrete não foi hoje (evita spam em caso de retry do job)
                if (cobranca.UltimoLembreteEm.HasValue
                    && cobranca.UltimoLembreteEm.Value.Date == DateTime.UtcNow.Date)
                    continue;

                await EnviarEmailDunningAsync(emailService, notificador, usuarioRepo, assinatura, cobranca, tentativa + 1, ct);
                cobranca.RegistrarLembrete();
                await cobrancaRepo.UpdateAsync(cobranca);
                logger.LogInformation("Dunning lembrete #{Num} enviado. EmpresaId={EmpresaId}", tentativa + 1, assinatura.EmpresaId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro no dunning para cobrança {CobrancaId}", cobranca.Id);
            }
        }

        await unitOfWork.CommitAsync();
    }

    /// <summary>
    /// Cancela automaticamente assinaturas suspensas há mais de 30 dias sem pagamento.
    /// </summary>
    private async Task CancelarSuspensasAntigasAsync(CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var assinaturaRepo = scope.ServiceProvider.GetRequiredService<IAssinaturaEmpresaRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var todasSuspensas = (await assinaturaRepo.GetSuspensasAntigasAsync(30, ct)).ToList();

        foreach (var assinatura in todasSuspensas)
        {
            try
            {
                assinatura.Cancelar();
                await assinaturaRepo.UpdateAsync(assinatura);
                logger.LogInformation("Assinatura cancelada por inadimplência (30d+). EmpresaId={EmpresaId}", assinatura.EmpresaId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro ao cancelar assinatura {Id} por inadimplência", assinatura.Id);
            }
        }

        if (todasSuspensas.Count > 0)
            await unitOfWork.CommitAsync();
    }

    private async Task EnviarEmailDunningAsync(
        IEmailService emailService,
        INotificadorService notificador,
        IUsuarioRepository usuarioRepo,
        Domain.Entities.AssinaturaEmpresa assinatura,
        Domain.Entities.CobrancaAssinatura cobranca,
        int numeroLembrete,
        CancellationToken ct)
    {
        try
        {
            var (usuarios, _) = await usuarioRepo.GetByEmpresaAsync(assinatura.EmpresaId, 1, 1);
            var admin = usuarios.FirstOrDefault();
            if (admin is null) return;

            if (_options.UseLegacyEmailAlerts)
            {
                var valorFormatado = cobranca.Valor.ToString("C", new System.Globalization.CultureInfo("pt-BR"));
                var urgencia = numeroLembrete switch
                {
                    1 => "Seu acesso está suspenso.",
                    2 => "⚠️ Segundo aviso — regularize sua assinatura.",
                    _ => "🚨 Último aviso antes do cancelamento definitivo."
                };
                var qrCodeImg = string.IsNullOrEmpty(cobranca.QrCodeBase64)
                    ? string.Empty
                    : $"<img src=\"data:image/png;base64,{cobranca.QrCodeBase64}\" alt=\"QR Code Pix\" style=\"width:200px;height:200px\" />";

                var body = $@"
<html><body style=""font-family:sans-serif;max-width:600px;margin:auto"">
<h2 style=""color:#dc2626"">EasyStock — Pagamento pendente</h2>
<p>Olá, <strong>{admin.Nome}</strong>!</p>
<p><strong>{urgencia}</strong></p>
<p>Regularize o pagamento de <strong>{valorFormatado}</strong> via Pix para restaurar o acesso ao EasyStock:</p>
{qrCodeImg}
<p style=""margin-top:16px""><strong>Pix Copia e Cola:</strong></p>
<pre style=""background:#f3f4f6;padding:12px;border-radius:6px;word-break:break-all;font-size:12px"">{cobranca.PixCopiaCola}</pre>
<p style=""color:#6b7280;font-size:12px"">Após o pagamento seu acesso será restaurado automaticamente em minutos.</p>
</body></html>";

                await emailService.SendAsync(admin.Email, $"EasyStock — Pagamento pendente (aviso {numeroLembrete}/3)", body, isHtml: true);
            }
            else
            {
                var urgencia = numeroLembrete switch
                {
                    1 => "Seu acesso está suspenso.",
                    2 => "Segundo aviso — regularize sua assinatura.",
                    _ => "Último aviso antes do cancelamento definitivo."
                };
                var payload = System.Text.Json.JsonSerializer.Serialize(new
                {
                    nome = admin.Nome,
                    email = admin.Email,
                    numero_lembrete = numeroLembrete,
                    urgencia,
                    valor = cobranca.Valor.ToString("C", new System.Globalization.CultureInfo("pt-BR")),
                    pix_copia_cola = cobranca.PixCopiaCola ?? string.Empty,
                    qr_code = cobranca.QrCodeBase64 ?? string.Empty
                });
                await notificador.PublicarEventoAsync(
                    TipoEventoNotificacao.AssinaturaExpirada,
                    assinatura.EmpresaId,
                    admin.Id,
                    payload,
                    ct: ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao enviar dunning #{Num} para empresa {EmpresaId}", numeroLembrete, assinatura.EmpresaId);
        }
    }

    private async Task EnviarEmailCobrancaAsync(
        IEmailService emailService,
        INotificadorService notificador,
        IUsuarioRepository usuarioRepo,
        AssinaturaEmpresa assinatura,
        CobrancaAssinatura cobranca,
        CancellationToken ct)
    {
        try
        {
            var (usuarios, _) = await usuarioRepo.GetByEmpresaAsync(assinatura.EmpresaId, 1, 1);
            var admin = usuarios.FirstOrDefault();
            if (admin is null) return;

            if (_options.UseLegacyEmailAlerts)
            {
                var vencimento = (assinatura.TrialFim ?? assinatura.DataFim)?.ToString("dd/MM/yyyy") ?? "em breve";
                var valorFormatado = cobranca.Valor.ToString("C", new System.Globalization.CultureInfo("pt-BR"));
                var qrCodeImg = string.IsNullOrEmpty(cobranca.QrCodeBase64)
                    ? string.Empty
                    : $"<img src=\"data:image/png;base64,{cobranca.QrCodeBase64}\" alt=\"QR Code Pix\" style=\"width:200px;height:200px\" />";

                var body = $@"
<html><body style=""font-family:sans-serif;max-width:600px;margin:auto"">
<h2 style=""color:#4f46e5"">Renovação da sua assinatura EasyStock</h2>
<p>Olá, <strong>{admin.Nome}</strong>!</p>
<p>Sua assinatura vence em <strong>{vencimento}</strong>. Para continuar usando o EasyStock sem interrupções, realize o pagamento via Pix:</p>
<p><strong>Valor:</strong> {valorFormatado}</p>
{qrCodeImg}
<p style=""margin-top:16px""><strong>Pix Copia e Cola:</strong></p>
<pre style=""background:#f3f4f6;padding:12px;border-radius:6px;word-break:break-all;font-size:12px"">{cobranca.PixCopiaCola}</pre>
<p style=""color:#6b7280;font-size:12px"">Após o pagamento, sua assinatura será renovada automaticamente por 30 dias.</p>
</body></html>";

                await emailService.SendAsync(admin.Email, "Renovação da sua assinatura EasyStock", body, isHtml: true);
                logger.LogInformation("Email de cobrança enviado para {Email}, empresa {EmpresaId}", admin.Email, assinatura.EmpresaId);
            }
            else
            {
                var payload = System.Text.Json.JsonSerializer.Serialize(new
                {
                    nome = admin.Nome,
                    email = admin.Email,
                    vencimento = (assinatura.TrialFim ?? assinatura.DataFim)?.ToString("dd/MM/yyyy") ?? "em breve",
                    valor = cobranca.Valor.ToString("C", new System.Globalization.CultureInfo("pt-BR")),
                    pix_copia_cola = cobranca.PixCopiaCola ?? string.Empty,
                    qr_code = cobranca.QrCodeBase64 ?? string.Empty
                });
                await notificador.PublicarEventoAsync(
                    TipoEventoNotificacao.AssinaturaExpirando,
                    assinatura.EmpresaId,
                    admin.Id,
                    payload,
                    ct: ct);
                logger.LogInformation("Evento AssinaturaExpirando publicado para empresa {EmpresaId}", assinatura.EmpresaId);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao notificar cobrança para empresa {EmpresaId}", assinatura.EmpresaId);
        }
    }
}

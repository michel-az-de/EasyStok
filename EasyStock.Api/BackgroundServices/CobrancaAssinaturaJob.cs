using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.BackgroundServices;

public sealed class CobrancaAssinaturaJob(
    IServiceProvider serviceProvider,
    ILogger<CobrancaAssinaturaJob> logger) : BackgroundService
{
    private const long LockKeyJob = 0x4B69_6C6C_4561_7379L; // arbitrary stable key

    private async Task RunWithAdvisoryLockAsync(long key, Func<CancellationToken, Task> action, CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetService<EasyStockDbContext>();
        if (db is null) { await action(ct); return; }

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
                await RunWithAdvisoryLockAsync(LockKeyJob, async ct =>
                {
                    await ProcessarCobrancasAsync(ct);
                    await SuspenderVencidasAsync(ct);
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
        var usuarioRepo = scope.ServiceProvider.GetRequiredService<IUsuarioRepository>();
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

                await EnviarEmailCobrancaAsync(emailService, usuarioRepo, assinatura, cobranca, ct);
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

    private async Task EnviarEmailCobrancaAsync(
        IEmailService emailService,
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
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao enviar email de cobrança para empresa {EmpresaId}", assinatura.EmpresaId);
        }
    }
}

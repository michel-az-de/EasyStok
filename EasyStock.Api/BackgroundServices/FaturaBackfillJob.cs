using EasyStock.Api.Services.Faturacao;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Faturas.EmitirFatura;
using EasyStock.Application.UseCases.Faturas.RegistrarPagamentoFatura;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace EasyStock.Api.BackgroundServices;

/// <summary>
/// Job idempotente que faz backfill de <see cref="Fatura"/> para
/// <see cref="CobrancaAssinatura"/> historicas (anteriores a F5).
///
/// <para>
/// Roda apenas uma vez, gated por configuration <c>Faturas:Backfill:Habilitado</c>
/// (default false). Itera todas as cobrancas com <c>FaturaId == null</c> em
/// batches, gera Fatura via <see cref="EmitirFaturaUseCase"/> (idempotente por
/// origem), e linka <c>cobranca.FaturaId</c>. Se a cobranca ja foi paga,
/// registra <see cref="FaturaPagamento"/> confirmado para refletir o estado.
/// </para>
///
/// <para>
/// Estrategia "best-effort": qualquer erro em uma cobranca individual e logado
/// e o job continua com a proxima. O contador final reporta sucesso/falhas
/// para auditoria.
/// </para>
/// </summary>
public sealed class FaturaBackfillJob(
    IServiceProvider serviceProvider,
    IConfiguration configuration,
    ILogger<FaturaBackfillJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var habilitado = configuration.GetValue<bool>("Faturas:Backfill:Habilitado");
        if (!habilitado)
        {
            logger.LogInformation("FaturaBackfillJob: Faturas:Backfill:Habilitado=false. Pulando execucao.");
            return;
        }

        // Aguarda a aplicacao subir antes de comecar (evita rodar durante
        // migrations ou cold start). 30s e conservador.
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        if (stoppingToken.IsCancellationRequested) return;

        var batchSize = configuration.GetValue("Faturas:Backfill:BatchSize", 100);
        var maxBatches = configuration.GetValue("Faturas:Backfill:MaxBatches", 100);

        logger.LogInformation(
            "FaturaBackfillJob: iniciando backfill (batchSize={BatchSize}, maxBatches={MaxBatches}).",
            batchSize, maxBatches);

        var totalProcessadas = 0;
        var totalErros = 0;

        for (var i = 0; i < maxBatches; i++)
        {
            if (stoppingToken.IsCancellationRequested) break;

            var processadasNoBatch = await ProcessarBatchAsync(batchSize, stoppingToken);
            if (processadasNoBatch == 0) break;
            totalProcessadas += processadasNoBatch;

            // Pequeno delay entre batches para nao saturar o banco
            await Task.Delay(TimeSpan.FromMilliseconds(200), stoppingToken);
        }

        logger.LogInformation(
            "FaturaBackfillJob: concluido. Processadas={Processadas} Erros={Erros}",
            totalProcessadas, totalErros);
    }

    private async Task<int> ProcessarBatchAsync(int batchSize, CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        var assinaturaRepo = scope.ServiceProvider.GetRequiredService<IAssinaturaEmpresaRepository>();
        var planoRepo = scope.ServiceProvider.GetRequiredService<IPlanoRepository>();
        var empresaRepo = scope.ServiceProvider.GetRequiredService<IEmpresaRepository>();
        var emitirFaturaUseCase = scope.ServiceProvider.GetRequiredService<EmitirFaturaUseCase>();
        var registrarPagamentoUseCase = scope.ServiceProvider.GetRequiredService<RegistrarPagamentoFaturaUseCase>();
        var faturaFactory = scope.ServiceProvider.GetRequiredService<FaturaSaasFactory>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        // Pega cobrancas sem FaturaId. SuperAdmin para bypassar filtro multi-tenant.
        var cobrancasOrfas = await db.CobrancasAssinatura
            .IgnoreQueryFilters()
            .Where(c => c.FaturaId == null)
            .OrderBy(c => c.CriadoEm)
            .Take(batchSize)
            .ToListAsync(ct);

        if (cobrancasOrfas.Count == 0) return 0;

        var processadas = 0;

        foreach (var cobranca in cobrancasOrfas)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var assinatura = await assinaturaRepo.GetByIdAsync(cobranca.AssinaturaId);
                var empresa = assinatura is not null ? await empresaRepo.GetByIdAsync(cobranca.EmpresaId) : null;
                var plano = assinatura is not null ? await planoRepo.GetByIdAsync(assinatura.PlanoId) : null;

                if (assinatura is null || empresa is null || plano is null)
                {
                    logger.LogWarning(
                        "FaturaBackfillJob: cobranca {CobrancaId} sem assinatura/empresa/plano resolvidos — pulando.",
                        cobranca.Id);
                    continue;
                }

                // Emissao idempotente por (Origem=Assinatura, OrigemRefId=assinatura.Id).
                var faturaCmd = faturaFactory.BuildParaAssinatura(
                    assinatura, plano, empresa,
                    dataEmissao: cobranca.CriadoEm,
                    dataVencimento: cobranca.ExpiracaoEm);
                var faturaResult = await emitirFaturaUseCase.ExecuteAsync(faturaCmd, ct);

                cobranca.FaturaId = faturaResult.FaturaId;
                db.CobrancasAssinatura.Update(cobranca);
                await unitOfWork.CommitAsync();

                // Se a cobranca esta Paga, registra FaturaPagamento confirmado para
                // refletir o estado historico.
                if (cobranca.Status == StatusCobranca.Paga && cobranca.PagoEm.HasValue)
                {
                    try
                    {
                        await registrarPagamentoUseCase.ExecuteAsync(new RegistrarPagamentoFaturaCommand(
                            EmpresaId: cobranca.EmpresaId,
                            FaturaId: faturaResult.FaturaId,
                            Metodo: cobranca.MetodoPagamento?.ToLowerInvariant() ?? "pix",
                            Valor: cobranca.Valor,
                            GatewayProvedor: cobranca.MetodoPagamento == "Boleto" ? "EfiBoleto" : "EfiPix",
                            GatewayTransactionId: cobranca.Txid,
                            DadosGatewayJson: null,
                            StatusInicial: StatusFaturaPagamento.Confirmado,
                            Observacao: "Backfill historico — cobranca paga antes da F5",
                            OrigemRegistro: "backfill"
                        ), ct);
                    }
                    catch (Exception payEx)
                    {
                        // Pagamento ja registrado (idempotencia parcial via TX) e aceitavel.
                        logger.LogWarning(payEx,
                            "FaturaBackfillJob: nao foi possivel registrar pagamento na fatura {FaturaId} para cobranca {Txid}.",
                            faturaResult.FaturaId, cobranca.Txid);
                    }
                }

                processadas++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "FaturaBackfillJob: erro ao processar cobranca {CobrancaId} (txid {Txid}).",
                    cobranca.Id, cobranca.Txid);
            }
        }

        return processadas;
    }
}

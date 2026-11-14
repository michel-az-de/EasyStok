using EasyStock.Application.UseCases.Financeiro.Pagamentos;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Api.BackgroundServices;

/// <summary>
/// Job horario de reconciliacao Pix para parcelas de Conta a Receber.
///
/// <para>
/// Por que existe: webhook Pix do Efi pode ser perdido (rede, retry exhausted,
/// processamento crashou apos 2xx). Job consulta gateway pra fechar gaps em
/// parcelas com EfiTxid associado mas ainda Pendente/ParcialmentePaga.
/// </para>
///
/// <para>
/// Roda hora em hora. Idempotencia via <c>ReconciliarPixParcelaReceberUseCase</c>:
/// se ja existe pagamento confirmado com mesmo Txid, no-op.
/// </para>
///
/// <para>Janela:</para> parcelas com Pix gerado nos ultimos 90 dias
/// (limite de estorno Efi).
/// </summary>
public sealed class ContaReceberPixReconciliacaoJob(
    IServiceProvider serviceProvider,
    ILogger<ContaReceberPixReconciliacaoJob> logger) : BackgroundService
{
    private const long LockKeyJob = 0x4361704361725069L; // "CapCarPi"
    private static readonly TimeSpan Intervalo = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("ContaReceberPixReconciliacaoJob iniciado");
        try { await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunWithAdvisoryLockAsync(LockKeyJob, ProcessarAsync, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "ContaReceberPixReconciliacaoJob: erro — aguardando 1h.");
            }

            try { await Task.Delay(Intervalo, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunWithAdvisoryLockAsync(long key, Func<CancellationToken, Task> action, CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetService<EasyStockDbContext>();
        if (db is null || !db.Database.IsNpgsql())
        {
            await action(ct);
            return;
        }

        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var conn = db.Database.GetDbConnection();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT pg_try_advisory_lock(@k)";
            var p = cmd.CreateParameter(); p.ParameterName = "k"; p.Value = key; cmd.Parameters.Add(p);

            var got = (bool?)await cmd.ExecuteScalarAsync(ct);
            if (got != true) return;
            try { await action(ct); }
            finally
            {
                await using var rel = conn.CreateCommand();
                rel.CommandText = "SELECT pg_advisory_unlock(@k)";
                var rp = rel.CreateParameter(); rp.ParameterName = "k"; rp.Value = key; rel.Parameters.Add(rp);
                await rel.ExecuteScalarAsync(ct);
            }
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }

    private async Task ProcessarAsync(CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var contaRepo = scope.ServiceProvider.GetRequiredService<IContaReceberRepository>();
        var reconciliarUseCase = scope.ServiceProvider.GetRequiredService<ReconciliarPixParcelaReceberUseCase>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var hoje = DateTime.UtcNow;
        var parcelas = await contaRepo.ListarParcelasComPixAtivoAsync(hoje, ct);

        var resumo = await RodadaPixContaReceber.ProcessarAsync(parcelas,
            async (txid, token) => (await reconciliarUseCase.ExecuteAsync(
                new ReconciliarPixParcelaReceberCommand(txid, ValorPagoEfi: null, PagoEm: null), token)).Reconciliado,
            unitOfWork.DescartarAlteracoesPendentes, logger, ct);

        logger.LogInformation(
            "ContaReceberPixReconciliacaoJob: parcelas={Total} consultadas={Consultadas} reconciliadas={Reconciliadas} falhas={Falhas}",
            parcelas.Count, resumo.Consultadas, resumo.Reconciliadas, resumo.Falhas);
    }
}

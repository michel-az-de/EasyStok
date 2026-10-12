using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Api.BackgroundServices;

/// <summary>
/// Expiração da cobrança do pedido (S11). A cada 60 s lista as cobranças online pendentes com o link
/// vencido (cross-tenant) e processa cada uma no próprio escopo, com o tenant dela ligado, pelo
/// <see cref="ProcessarCobrancaVencidaUseCase"/>: reemite uma vez para pedido da conversa; cancela o
/// pedido (e libera a vaga) na segunda expiração ou quando o pedido é do site.
///
/// <para>
/// Advisory lock evita duas instâncias processando a mesma rodada (mesmo padrão do
/// <see cref="ContaReceberPixReconciliacaoJob"/>). Desligado por <c>BackgroundJobs:EnableCobrancaPedido=false</c>.
/// </para>
///
/// <para>
/// S32 acrescenta, antes de expirar, a consulta <c>GET v1/payments/search?external_reference=</c> para
/// pegar webhook perdido.
/// </para>
/// </summary>
public sealed class CobrancaPedidoJob(
    IServiceProvider serviceProvider,
    TimeProvider relogio,
    ILogger<CobrancaPedidoJob> logger) : BackgroundService
{
    private const long LockKeyJob = 0x436F6250656449L; // "CobPedI"
    private const int MaximoPorRodada = 50;
    private static readonly TimeSpan Intervalo = TimeSpan.FromSeconds(60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("CobrancaPedidoJob iniciado");
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunWithAdvisoryLockAsync(ProcessarRodadaAsync, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "CobrancaPedidoJob: erro na rodada.");
            }

            try { await Task.Delay(Intervalo, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunWithAdvisoryLockAsync(Func<CancellationToken, Task> action, CancellationToken ct)
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
            var p = cmd.CreateParameter(); p.ParameterName = "k"; p.Value = LockKeyJob; cmd.Parameters.Add(p);

            var got = (bool?)await cmd.ExecuteScalarAsync(ct);
            if (got != true) return;
            try { await action(ct); }
            finally
            {
                await using var rel = conn.CreateCommand();
                rel.CommandText = "SELECT pg_advisory_unlock(@k)";
                var rp = rel.CreateParameter(); rp.ParameterName = "k"; rp.Value = LockKeyJob; rel.Parameters.Add(rp);
                await rel.ExecuteScalarAsync(ct);
            }
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }

    private async Task ProcessarRodadaAsync(CancellationToken ct)
    {
        IReadOnlyList<CobrancaPedidoVencida> vencidas;
        using (var scope = serviceProvider.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<ICobrancaPedidoRepository>();
            vencidas = await repo.ListarPendentesVencidasAsync(relogio.GetUtcNow().UtcDateTime, MaximoPorRodada, ct);
        }
        if (vencidas.Count == 0) return;

        var falhas = 0;
        foreach (var item in vencidas)
        {
            try
            {
                // Um escopo por cobrança: DbContext limpo e o tenant da cobrança só nele.
                using var scope = serviceProvider.CreateScope();
                var useCase = scope.ServiceProvider.GetRequiredService<ProcessarCobrancaVencidaUseCase>();
                await useCase.ExecuteAsync(item, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                falhas++;
                logger.LogWarning(ex, "CobrancaPedidoJob: falha na cobranca {CobrancaId} pedido {PedidoId}",
                    item.CobrancaId, item.PedidoId);
                if (falhas >= 3)
                {
                    logger.LogWarning("CobrancaPedidoJob: 3 falhas na rodada — interrompendo.");
                    break;
                }
            }
        }

        logger.LogInformation("CobrancaPedidoJob: vencidas={Total} falhas={Falhas}", vencidas.Count, falhas);
    }
}

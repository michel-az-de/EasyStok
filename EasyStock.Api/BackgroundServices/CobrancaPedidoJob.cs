using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Application.UseCases.Atendimento.Automacoes;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Api.BackgroundServices;

/// <summary>
/// Expiração da cobrança do pedido (S11). A cada 60 s lista as cobranças online pendentes com o link
/// vencido (cross-tenant) e processa cada uma no próprio escopo, com o tenant dela ligado, pelo
/// <see cref="ProcessarCobrancaVencidaUseCase"/>: reemite uma vez para pedido da conversa; cancela o
/// pedido (e libera a vaga) na segunda expiração ou quando o pedido é do site. Na mesma rodada, lembra o cliente
/// dos links da conversa a 10 min de vencer (#1610, <see cref="LembrarCobrancaVencendoUseCase"/>).
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
    private int _pularVencidas;
    private DateTime? _limiteVencidas;
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
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
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
        await ProcessarVencidasAsync(ct);
        await LembrarVencendoAsync(ct);
    }

    private async Task ProcessarVencidasAsync(CancellationToken ct)
    {
        IReadOnlyList<CobrancaPedidoVencida> vencidas;
        using (var scope = serviceProvider.CreateScope())
        {
            // I3.2: credencial de volta devolve à varredura as cobranças que esperavam por ela.
            await scope.ServiceProvider.GetRequiredService<RetomarCobrancasAguardandoCredencialUseCase>().ExecuteAsync(ct);

            var repo = scope.ServiceProvider.GetRequiredService<ICobrancaPedidoRepository>();
            // #2057: novos vencimentos não estendem a varredura sem fim. É um corte temporal,
            // não uma fotografia das linhas: remoções continuam visíveis à paginação.
            var limite = _limiteVencidas ?? relogio.GetUtcNow().UtcDateTime;
            vencidas = await repo.ListarPendentesVencidasAsync(limite, MaximoPorRodada, _pularVencidas, ct);
            _limiteVencidas = limite;
        }
        if (vencidas.Count == 0)
        {
            _pularVencidas = 0;
            _limiteVencidas = null;
            return;
        }

        // Um escopo por cobrança: DbContext limpo e o tenant da cobrança só nele. Uma falha não pula a empresa.
        var resumo = await RodadaDeCobrancasVencidas.ProcessarAsync(vencidas, async (item, token) =>
        {
            using var scope = serviceProvider.CreateScope();
            var useCase = scope.ServiceProvider.GetRequiredService<ProcessarCobrancaVencidaUseCase>();
            await useCase.ExecuteAsync(item, token);
        }, logger, ct);

        // #2048: a próxima rodada alcança outra página mesmo se as 50 antigas falharem. Itens que saem
        // da consulta podem adiar outros até a volta ao início; reiniciar o job também volta ao início.
        // O cursor só avança depois de concluir a rodada: cancelamento não pula uma página pela metade.
        _pularVencidas = vencidas.Count == MaximoPorRodada ? _pularVencidas + vencidas.Count : 0;
        if (_pularVencidas == 0) _limiteVencidas = null;

        logger.LogInformation("CobrancaPedidoJob: vencidas={Total} falhas={Falhas} puladas={Puladas}",
            vencidas.Count, resumo.Falhas, resumo.Puladas);
    }

    /// <summary>
    /// #1610: lembrete ao cliente nos últimos minutos do link (só avisa). Roda depois das vencidas, para não atrasá-las,
    /// e com a própria proteção: falha da consulta ou de uma cobrança não derruba a rodada. Um escopo por cobrança.
    /// </summary>
    private async Task LembrarVencendoAsync(CancellationToken ct)
    {
        IReadOnlyList<CobrancaPedidoVencendo> vencendo;
        try
        {
            using var scope = serviceProvider.CreateScope();
            vencendo = await scope.ServiceProvider.GetRequiredService<ICobrancaPedidoRepository>()
                .ListarPendentesVencendoAsync(relogio.GetUtcNow().UtcDateTime, LembrarCobrancaVencendoUseCase.Antecedencia, MaximoPorRodada, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "CobrancaPedidoJob: consulta dos links perto de vencer falhou");
            return;
        }

        foreach (var item in vencendo)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var scope = serviceProvider.CreateScope();
                await scope.ServiceProvider.GetRequiredService<LembrarCobrancaVencendoUseCase>().ExecuteAsync(item, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "CobrancaPedidoJob: lembrete da cobranca {CobrancaId} pedido {PedidoId} falhou",
                    item.CobrancaId, item.PedidoId);
            }
        }
    }
}
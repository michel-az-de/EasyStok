using EasyStock.Infra.Postgre.Concurrency;

namespace EasyStock.Api.BackgroundServices;

/// <summary>
/// Recalcula a velocidade de saída (média diária dos últimos 30 dias) dos lotes com saldo, a cada 6 h (#2016). Roda
/// <b>por empresa</b>, cada uma num escopo com o tenant ligado (<see cref="RodadaPorEmpresa"/>): sem isso o filtro global
/// zerava a leitura e o job processava 0 lote (#1588). O advisory lock deixa uma réplica só na rodada.
///
/// <para>
/// Lê os lotes RASTREADOS e deixa o rastreador gravar só o que mudou; o <c>Update</c> do grafo inteiro, que a versão
/// anterior fazia sobre lotes lidos com <c>Include</c>, reescreveria produto e variação. Lote com a mesma velocidade não
/// é gravado. Um lote mexido por uma venda no meio da página (token <c>xmin</c>) descarta a página e ela volta na rodada
/// seguinte.
/// </para>
///
/// <para>Desligado por <c>BackgroundJobs:EnableRecalcularVelocidadesJob=false</c>.</para>
/// </summary>
public sealed class RecalcularVelocidadesJob(
    IServiceScopeFactory scopeFactory,
    ILogger<RecalcularVelocidadesJob> logger) : BackgroundService
{
    public static readonly TimeSpan Intervalo = TimeSpan.FromHours(6);
    private static readonly TimeSpan EsperaInicial = TimeSpan.FromMinutes(2);
    private const int TamanhoDaPagina = 500;
    private const int JanelaEmDias = 30;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Job de recálculo de velocidades iniciado — intervalo={Intervalo}.", Intervalo);

        try { await Task.Delay(EsperaInicial, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RodadaAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro no recálculo de velocidades — tenta de novo em {Intervalo}.", Intervalo);
            }

            try { await Task.Delay(Intervalo, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Uma rodada com a trava. Pública para os testes de integração; o laço de 6 em 6 h a chama.</summary>
    public Task<bool> RodadaAsync(CancellationToken ct) => PostgresAdvisoryLock.TentarExecutarEmEscopoProprioAsync(
        scopeFactory,
        LockKeys.RecalcularVelocidades,
        async token =>
        {
            var resultado = await RodadaPorEmpresa.ExecutarAsync(
                scopeFactory,
                logger,
                nameof(RecalcularVelocidadesJob),
                RecalcularEmpresaAsync,
                token);
            logger.LogInformation(
                "Velocidades: {Empresas} empresa(s) percorrida(s), {Falhas} com falha.", resultado.Empresas, resultado.Falhas);
        },
        ct);

    private async Task RecalcularEmpresaAsync(IServiceProvider sp, Guid empresaId, CancellationToken ct)
    {
        var itemEstoqueRepo = sp.GetRequiredService<IItemEstoqueRepository>();
        var movimentacaoRepo = sp.GetRequiredService<IMovimentacaoEstoqueRepository>();
        var unitOfWork = sp.GetRequiredService<IUnitOfWork>();

        var ate = DateTime.UtcNow;
        var de = ate.AddDays(-JanelaEmDias);
        var pagina = 1;
        var lidos = 0;
        var atualizados = 0;
        IReadOnlyList<ItemEstoque> lotes;

        do
        {
            lotes = await itemEstoqueRepo.GetLotesComSaldoRastreadosAsync(empresaId, pagina, TamanhoDaPagina, ct);
            if (lotes.Count == 0)
                break;

            // Uma consulta das taxas por página, no lugar de uma por lote.
            var taxas = await movimentacaoRepo.GetTaxaSaidaDiariaPorProdutoAsync(empresaId, lotes.Select(l => l.ProdutoId), de, ate);

            var mudados = 0;
            foreach (var lote in lotes)
            {
                var velocidade = Math.Max(0m, decimal.Round(taxas.GetValueOrDefault(lote.ProdutoId, 0m), 2));
                if (velocidade == lote.VelocidadeSaidaDiaria)
                    continue;

                lote.AtualizarVelocidadeSaida(velocidade, ate);
                lote.AlteradoEm = ate;
                mudados++;
            }

            try
            {
                if (mudados > 0)
                    await unitOfWork.CommitAsync();
                atualizados += mudados;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                logger.LogWarning(ex, "Velocidades da empresa {EmpresaId}: página {Pagina} mexida por outra operação; volta na próxima rodada.", empresaId, pagina);
                unitOfWork.DescartarAlteracoesPendentes();
            }

            // A página seguinte não precisa dos lotes desta no rastreador.
            unitOfWork.DescartarAlteracoesPendentes();
            lidos += lotes.Count;
            pagina++;
        }
        while (lotes.Count == TamanhoDaPagina);

        logger.LogInformation("Velocidades da empresa {EmpresaId}: {Lidos} lote(s) lido(s), {Atualizados} atualizado(s).", empresaId, lidos, atualizados);
    }
}

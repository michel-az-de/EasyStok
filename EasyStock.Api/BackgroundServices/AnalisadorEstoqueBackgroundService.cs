using EasyStock.Infra.Postgre.Concurrency;

namespace EasyStock.Api.BackgroundServices;

/// <summary>
/// Alertas de estoque para o sino da equipe, de hora em hora (#2016): validade próxima e vencida, abaixo do mínimo,
/// reposição, produto parado e pedido de compra (<see cref="GeradorNotificacoesAutomaticas"/>), mais a marcação do lote
/// vencido. Roda <b>por empresa</b>, cada uma num escopo com o tenant ligado (<see cref="RodadaPorEmpresa"/>): sem isso o
/// filtro global zerava toda leitura e o job não fazia nada (#1588). O advisory lock deixa uma réplica só na rodada.
///
/// <para>
/// Ligado por padrão; <c>BackgroundJobs:EnableAnalisadorEstoque=false</c> desliga sem deploy de código. O alerta é um por
/// item por dia de Brasília: rodar de novo no mesmo dia (reinício, outra réplica) não repete.
/// </para>
/// </summary>
public sealed class AnalisadorEstoqueBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<AnalisadorEstoqueBackgroundService> logger)
    : BackgroundService
{
    public static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan EsperaInicial = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("AnalisadorEstoqueBackgroundService iniciado — intervalo={Intervalo}.", Intervalo);

        // Deixa o app terminar de subir (migrações, seed) antes da primeira rodada.
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
                logger.LogError(ex, "AnalisadorEstoqueBackgroundService: erro na rodada — tenta de novo em {Intervalo}.", Intervalo);
            }

            try { await Task.Delay(Intervalo, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Uma rodada com a trava. Pública para os testes de integração; o laço de hora em hora a chama.</summary>
    public Task<bool> RodadaAsync(CancellationToken ct) => PostgresAdvisoryLock.TentarExecutarEmEscopoProprioAsync(
        scopeFactory,
        LockKeys.AlertasEstoque,
        async token =>
        {
            var resultado = await RodadaPorEmpresa.ExecutarAsync(
                scopeFactory,
                logger,
                nameof(AnalisadorEstoqueBackgroundService),
                (sp, empresaId, tokenDaEmpresa) => sp.GetRequiredService<GeradorNotificacoesAutomaticas>().ExecutarAsync(empresaId, tokenDaEmpresa),
                token);
            logger.LogInformation(
                "Alertas de estoque: {Empresas} empresa(s) percorrida(s), {Falhas} com falha.", resultado.Empresas, resultado.Falhas);
        },
        ct);
}

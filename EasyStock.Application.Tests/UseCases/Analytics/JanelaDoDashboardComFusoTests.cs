using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Analytics.DashboardExtras;
using EasyStock.Application.UseCases.Analytics.ReceitaCusto;

namespace EasyStock.Application.Tests.UseCases.Analytics;

/// <summary>
/// Issue #2561: o fuso do cliente (tz = getTimezoneOffset, BRT = 180) só serve para agrupar.
/// A janela é de instantes UTC e precisa terminar AGORA; com <c>UtcNow.AddMinutes(-tz)</c> ela
/// fechava 3h antes e cortava as vendas mais recentes.
/// </summary>
public class JanelaDoDashboardComFusoTests
{
    private readonly IAnalyticsRepository _repository = Substitute.For<IAnalyticsRepository>();

    private static bool EhAgora(DateTime ate) => Math.Abs((DateTime.UtcNow - ate).TotalMinutes) < 5;

    [Fact]
    public async Task ReceitaCusto_com_fuso_de_brasilia_termina_a_janela_agora()
    {
        var empresaId = Guid.NewGuid();

        await new GetReceitaCustoUseCase(_repository)
            .ExecuteAsync(new GetReceitaCustoCommand(empresaId, 30, null, 180));

        await _repository.Received(1).GetReceitaCustoSerieAsync(
            empresaId, Arg.Any<DateTime>(), Arg.Is<DateTime>(ate => EhAgora(ate)), null, 180);
    }

    [Fact]
    public async Task DashboardExtras_com_fuso_de_brasilia_termina_a_janela_agora()
    {
        var empresaId = Guid.NewGuid();

        await new GetDashboardExtrasUseCase(_repository)
            .ExecuteAsync(new GetDashboardExtrasCommand(empresaId, 30, null, 180));

        await _repository.Received(1).GetTopProdutosAsync(
            empresaId, Arg.Any<DateTime>(), Arg.Is<DateTime>(ate => EhAgora(ate)), 5, null);
    }
}

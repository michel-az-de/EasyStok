using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Analytics.Dia;

namespace EasyStock.Application.Tests.UseCases.Analytics;

public class ObterResumoDiaResultTests
{
    // 1.1-B: garante que os counts do checklist de ativacao chegam ao result
    // nas posicoes certas. Argumentos nomeados no DTO pegam inversao de ordem
    // posicional entre CategoriasCount e EntradasCount.
    [Fact]
    public void FromDto_CopiaCategoriasCountEEntradasCount_NasPosicoesCertas()
    {
        var dto = new ResumoDia(
            PedidosEntreguesHoje: 1,
            FaturamentoHoje: 10m,
            TicketMedioHoje: 10m,
            PedidosPendentes: 2,
            ValorPedidosPendentes: 20m,
            CaixaAbertaHoje: true,
            CaixaFechadaHoje: false,
            SaldoCaixaAtual: 30m,
            PixRecebidosHoje: 3,
            ValorPixHoje: 40m,
            OnboardingCompleto: true,
            CategoriasCount: 7,
            EntradasCount: 9);

        var result = ObterResumoDiaResult.FromDto(dto);

        result.CategoriasCount.Should().Be(7);
        result.EntradasCount.Should().Be(9);
    }
}

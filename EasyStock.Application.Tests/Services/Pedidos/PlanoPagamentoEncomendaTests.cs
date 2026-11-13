using EasyStock.Application.Services.Pedidos;

namespace EasyStock.Application.Tests.Services.Pedidos;

/// <summary>
/// #2259: o sinal automático de 50% vale só para encomenda com 2 dias ou mais de antecedência, contados na data de
/// Brasília. Pedido para o mesmo dia ou para o dia seguinte paga o valor integral.
/// </summary>
public class PlanoPagamentoEncomendaTests
{
    // 05/11/2026 12:00 em Brasília (UTC-3).
    private static readonly DateTime Agora = new(2026, 11, 5, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void TemSinal_SemAgendamento_NaoCobraSinal() =>
        PlanoPagamentoEncomenda.TemSinal(null, Agora).Should().BeFalse();

    [Theory]
    [InlineData(0)] // mesmo dia
    [InlineData(1)] // dia seguinte: valor integral
    public void TemSinal_EntregaAteODiaSeguinte_PagaIntegral(int dias) =>
        PlanoPagamentoEncomenda.TemSinal(Agora.AddDays(dias), Agora).Should().BeFalse();

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    public void TemSinal_EntregaComDoisDiasOuMais_CobraSinal(int dias) =>
        PlanoPagamentoEncomenda.TemSinal(Agora.AddDays(dias), Agora).Should().BeTrue();

    [Fact]
    public void TemSinal_ContaADataDeBrasiliaNaoADataUtc()
    {
        // 05/11 23:30 em Brasília, já 06/11 em UTC.
        var tardeDaNoite = new DateTime(2026, 11, 6, 2, 30, 0, DateTimeKind.Utc);
        // 06/11 23:30 em Brasília (dia seguinte), já 07/11 em UTC.
        var diaSeguinteNoite = new DateTime(2026, 11, 7, 2, 30, 0, DateTimeKind.Utc);
        // 07/11 00:00 em Brasília (dois dias depois).
        var doisDiasMeiaNoite = new DateTime(2026, 11, 7, 3, 0, 0, DateTimeKind.Utc);

        PlanoPagamentoEncomenda.TemSinal(diaSeguinteNoite, tardeDaNoite).Should().BeFalse();
        PlanoPagamentoEncomenda.TemSinal(doisDiasMeiaNoite, tardeDaNoite).Should().BeTrue();
    }
}

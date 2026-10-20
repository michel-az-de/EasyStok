using EasyStock.Application.Common;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Caixa;
using EasyStock.Application.UseCases.ListarFechamentosCaixa;
using EasyStock.Application.UseCases.ObterCaixaDia;

namespace EasyStock.Application.Tests.UseCases.Caixa;

/// <summary>
/// #1776-11: a gaveta é só dinheiro. O <c>SaldoFinal</c> do fechamento soma Pix e cartão (é o caixa contábil do dia) e a
/// abertura seguinte sugeria esse valor como dinheiro na gaveta. O fechamento passa a informar também o dinheiro
/// físico, pela mesma conta de <see cref="GavetaCaixa"/> que o console usa no dia.
/// </summary>
public class ListarFechamentosCaixaUseCaseTests
{
    [Fact]
    public async Task FechamentoInformaODinheiroNaGaveta_SemPixECartao()
    {
        var empresaId = Guid.NewGuid();
        var dia = new DateOnly(2026, 10, 19);
        var repo = Substitute.For<ICaixaRepository>();
        var fechamento = FechamentoCaixa.Criar(empresaId, dia, saldoInicial: 10m, totalVendas: 150m, 0m, 0m, 0m);
        repo.ListFechamentosAsync(empresaId, 1, 7, null, null).Returns(((IEnumerable<FechamentoCaixa>)[fechamento], 1));
        var calc = Substitute.For<ICaixaSaldoCalculator>();
        var (ini, fim) = HorarioBrasil.JanelaDiaUtc(dia);
        calc.CalcularAsync(empresaId, dia, null).Returns(new CaixaSaldoBreakdown(
            dia, 10m, 150m, 0m, 0m, 0m, 160m, false, true, false, null, ini, fim, [], fechamento));
        calc.GetLinhasExtrasAsync(empresaId, ini, fim, null).Returns(
        [
            new CaixaLinhaExtraResult(ini.AddHours(12), "venda", 100m, "Bolo", "pix", "Venda"),
            new CaixaLinhaExtraResult(ini.AddHours(13), "venda", 50m, "Doce", "dinheiro", "Venda"),
        ]);

        var r = await new ListarFechamentosCaixaUseCase(repo, new ObterCaixaDiaUseCase(calc))
            .ExecuteAsync(new ListarFechamentosCaixaQuery(empresaId, 1, 7));

        var item = r.Items.Should().ContainSingle().Subject;
        item.SaldoFinal.Should().Be(160m, "o saldo contábil do dia continua o mesmo");
        item.DinheiroNaGaveta.Should().Be(60m, "10 de abertura + 50 em dinheiro; o Pix não está na gaveta");
    }
}

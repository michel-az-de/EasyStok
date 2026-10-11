using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Financeiro.Pagamentos;
using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Enums.Financeiro;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases.Financeiro;

/// <summary>
/// #1598: o estorno de pagamento de conta a pagar não toca o movimento de um dia que o Caixa da empresa
/// (sem loja) já fechou, mesmo quando o movimento tem loja. A reversão fica só no razão.
/// </summary>
public class EstornarPagamentoParcelaPagarUseCaseTests
{
    private static readonly Guid Empresa = Guid.NewGuid();

    [Fact]
    public async Task Dia_fechado_pela_empresa_NAO_toca_o_movimento_que_tem_loja()
    {
        var conta = ContaPagar.Criar(Empresa, null, Guid.NewGuid(), "Fornecedor", DateTime.UtcNow);
        conta.AdicionarParcela(1, 100m, DateTime.UtcNow.AddDays(30));
        conta.Emitir();
        var parcela = conta.Parcelas.First();
        parcela.ContaPagar = conta;
        var pag = PagamentoParcela.CriarConfirmado(Empresa, TipoLadoFinanceiro.Pagar, 100m, "pix", DateTime.UtcNow);
        parcela.RegistrarPagamento(pag, DateTime.UtcNow);
        conta.AtualizarStatusPorParcelas();
        var mov = MovimentoCaixa.Criar(Empresa, "saida", 100m, DateTime.UtcNow, lojaId: Guid.NewGuid());
        pag.AssociarMovimentoCaixa(mov.Id);

        var contaRepo = Substitute.For<IContaPagarRepository>();
        contaRepo.GetParcelaWithContaAsync(Empresa, parcela.Id, Arg.Any<CancellationToken>()).Returns(parcela);
        var caixaRepo = Substitute.For<ICaixaRepository>();
        caixaRepo.GetMovimentoAsync(Empresa, mov.Id).Returns(mov);
        caixaRepo.GetFechamentoDoDiaAsync(Empresa, Arg.Any<DateOnly>(), null)
            .Returns(FechamentoCaixa.Criar(Empresa, DateOnly.FromDateTime(DateTime.UtcNow), 0m, 0m, 0m, 0m, 0m, null));
        var uc = new EstornarPagamentoParcelaPagarUseCase(contaRepo, caixaRepo, Substitute.For<IUnitOfWork>(),
            Substitute.For<ILogger<EstornarPagamentoParcelaPagarUseCase>>());

        var ok = await uc.ExecuteAsync(new EstornarPagamentoParcelaPagarCommand(Empresa, parcela.Id, pag.Id, "Lancado por engano"));

        ok.Should().BeTrue();
        pag.Status.Should().Be(StatusPagamentoParcela.Estornado);
        mov.EstornadoEm.Should().BeNull();
        await caixaRepo.DidNotReceive().UpdateMovimentoAsync(Arg.Any<MovimentoCaixa>());
    }
}

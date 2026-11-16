using EasyStock.Application.Common;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Tests.Helpers;
using EasyStock.Application.UseCases.Caixa;
using EasyStock.Application.UseCases.Financeiro.Pagamentos;
using EasyStock.Application.UseCases.ObterCaixaDia;
using EasyStock.Domain.Entities.Financeiro;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases.Financeiro;

/// <summary>
/// #2736 (CXF-01): pagar ou receber parcela grava movimento no Caixa. A saída toma a mesma trava do lançamento
/// manual e do fechamento, e a conta a pagar em dinheiro não passa do dinheiro que está na gaveta. O dia fechado
/// NÃO recusa: o lançamento cai em PosFechamento por desenho (#2319).
/// </summary>
public class RegistrarPagamentoParcelaPagarTetoGavetaTests
{
    private static readonly Guid Empresa = Guid.NewGuid();

    private static ParcelaPagar Parcela(decimal valor)
    {
        var conta = ContaPagar.Criar(Empresa, null, Guid.NewGuid(), "Fornecedor", DateTime.UtcNow);
        conta.AdicionarParcela(1, valor, DateTime.UtcNow.AddDays(10));
        conta.Emitir();
        var parcela = conta.Parcelas.First();
        parcela.ContaPagar = conta;
        return parcela;
    }

    private static (RegistrarPagamentoParcelaPagarUseCase Uc, ICaixaRepository Caixa) Montar(ParcelaPagar parcela, decimal dinheiroNaGaveta, bool comAbertura = true)
    {
        var repo = Substitute.For<IContaPagarRepository>();
        repo.GetParcelaWithContaAsync(Empresa, parcela.Id, Arg.Any<CancellationToken>()).Returns(parcela);
        var caixa = Substitute.For<ICaixaRepository>();
        caixa.GetMovimentosDoDiaAsync(Empresa, Arg.Any<DateOnly>(), Arg.Any<Guid?>())
            .Returns(comAbertura ? new[] { MovimentoCaixa.Criar(Empresa, "abertura", dinheiroNaGaveta) } : Array.Empty<MovimentoCaixa>());
        var uow = Substitute.For<IUnitOfWork>().SetupExecuteInTransactionSemRetry<RegistrarPagamentoResult?>();
        var uc = new RegistrarPagamentoParcelaPagarUseCase(repo, caixa,
            new ObterCaixaDiaUseCase(new CaixaSaldoCalculator(caixa)), uow,
            Substitute.For<ILogger<RegistrarPagamentoParcelaPagarUseCase>>());
        return (uc, caixa);
    }

    private static RegistrarPagamentoParcelaPagarCommand Pagar(ParcelaPagar p, decimal valor, string metodo) =>
        new(Empresa, p.Id, valor, metodo);

    [Fact]
    public async Task Conta_a_pagar_em_dinheiro_acima_da_gaveta_e_recusada_sem_gravar_a_saida()
    {
        var parcela = Parcela(500m);
        var (uc, caixa) = Montar(parcela, dinheiroNaGaveta: 100m);

        var acao = () => uc.ExecuteAsync(Pagar(parcela, 500m, "dinheiro"));

        (await acao.Should().ThrowAsync<UseCaseValidationException>()).Which.Message.Should().Contain("gaveta");
        await caixa.DidNotReceive().AddMovimentoAsync(Arg.Any<MovimentoCaixa>());
    }

    [Fact]
    public async Task Conta_a_pagar_em_dinheiro_dentro_da_gaveta_e_paga()
    {
        var parcela = Parcela(80m);
        var (uc, caixa) = Montar(parcela, dinheiroNaGaveta: 100m);

        var r = await uc.ExecuteAsync(Pagar(parcela, 80m, "dinheiro"));

        r.Should().NotBeNull();
        await caixa.Received(1).AddMovimentoAsync(Arg.Is<MovimentoCaixa>(m => m.Tipo == "saida" && m.Valor == 80m));
    }

    [Fact]
    public async Task Conta_a_pagar_em_pix_nao_confere_a_gaveta()
    {
        var parcela = Parcela(500m);
        var (uc, caixa) = Montar(parcela, dinheiroNaGaveta: 100m);

        var r = await uc.ExecuteAsync(Pagar(parcela, 500m, "pix"));

        r.Should().NotBeNull();
        await caixa.Received(1).AddMovimentoAsync(Arg.Any<MovimentoCaixa>());
    }

    [Fact]
    public async Task Conta_a_pagar_toma_a_trava_do_caixa_antes_de_gravar_a_saida()
    {
        var parcela = Parcela(50m);
        var (uc, caixa) = Montar(parcela, dinheiroNaGaveta: 100m);

        await uc.ExecuteAsync(Pagar(parcela, 50m, "dinheiro"));

        Received.InOrder(() =>
        {
            caixa.TravarSaidasAsync(Empresa, parcela.ContaPagar!.LojaId, Arg.Any<CancellationToken>());
            caixa.AddMovimentoAsync(Arg.Any<MovimentoCaixa>());
        });
    }

    [Fact]
    public async Task Conta_a_pagar_com_o_dia_fechado_segue_paga_e_cai_em_pos_fechamento()
    {
        var parcela = Parcela(50m);
        var (uc, caixa) = Montar(parcela, dinheiroNaGaveta: 100m);
        caixa.GetFechamentoDoDiaAsync(Empresa, Arg.Any<DateOnly>(), Arg.Any<Guid?>())
            .Returns(FechamentoCaixa.Criar(Empresa, HorarioBrasil.Hoje(), 100, 0, 0, 0, 0));

        var r = await uc.ExecuteAsync(Pagar(parcela, 50m, "pix"));

        r.Should().NotBeNull();
        await caixa.Received(1).AddMovimentoAsync(Arg.Any<MovimentoCaixa>());
    }

    [Fact]
    public async Task Conta_a_pagar_em_dinheiro_com_o_dia_fechado_segue_paga_sem_conferir_o_teto()
    {
        var parcela = Parcela(500m);
        var (uc, caixa) = Montar(parcela, dinheiroNaGaveta: 100m);
        caixa.GetFechamentoDoDiaAsync(Empresa, Arg.Any<DateOnly>(), Arg.Any<Guid?>())
            .Returns(FechamentoCaixa.Criar(Empresa, HorarioBrasil.Hoje(), 100, 0, 0, 0, 0));

        var r = await uc.ExecuteAsync(Pagar(parcela, 500m, "dinheiro"));

        r.Should().NotBeNull();
        await caixa.Received(1).AddMovimentoAsync(Arg.Is<MovimentoCaixa>(m => m.Tipo == "saida" && m.Valor == 500m));
    }

    [Fact]
    public async Task Conta_a_pagar_em_dinheiro_com_o_caixa_nunca_aberto_segue_paga_sem_conferir_o_teto()
    {
        var parcela = Parcela(50m);
        var (uc, caixa) = Montar(parcela, dinheiroNaGaveta: 0m, comAbertura: false);

        var r = await uc.ExecuteAsync(Pagar(parcela, 50m, "dinheiro"));

        r.Should().NotBeNull();
        await caixa.Received(1).AddMovimentoAsync(Arg.Is<MovimentoCaixa>(m => m.Tipo == "saida" && m.Valor == 50m));
    }
}

public class RegistrarPagamentoParcelaReceberTravaTests
{
    private static readonly Guid Empresa = Guid.NewGuid();

    [Fact]
    public async Task Recebimento_toma_a_trava_do_caixa_antes_de_gravar_a_entrada()
    {
        var conta = ContaReceber.Criar(Empresa, null, Guid.NewGuid(), "Cliente", DateTime.UtcNow);
        conta.AdicionarParcela(1, 100m, DateTime.UtcNow.AddDays(10));
        conta.Emitir();
        var parcela = conta.Parcelas.First();
        parcela.ContaReceber = conta;
        var repo = Substitute.For<IContaReceberRepository>();
        repo.GetParcelaWithContaAsync(Empresa, parcela.Id, Arg.Any<CancellationToken>()).Returns(parcela);
        var caixa = Substitute.For<ICaixaRepository>();
        var uow = Substitute.For<IUnitOfWork>().SetupExecuteInTransactionSemRetry<RegistrarPagamentoResult?>();
        var uc = new RegistrarPagamentoParcelaReceberUseCase(repo, caixa, uow,
            Substitute.For<ILogger<RegistrarPagamentoParcelaReceberUseCase>>());

        var r = await uc.ExecuteAsync(new RegistrarPagamentoParcelaReceberCommand(Empresa, parcela.Id, 100m, "dinheiro"));

        r.Should().NotBeNull();
        Received.InOrder(() =>
        {
            caixa.TravarSaidasAsync(Empresa, conta.LojaId, Arg.Any<CancellationToken>());
            caixa.AddMovimentoAsync(Arg.Any<MovimentoCaixa>());
        });
    }
}

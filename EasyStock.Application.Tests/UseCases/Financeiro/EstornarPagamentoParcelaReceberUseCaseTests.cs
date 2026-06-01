using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Financeiro.Pagamentos;
using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Enums.Financeiro;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases.Financeiro;

/// <summary>
/// Trava o modelo SÓ-CONTÁBIL do estorno (decisão round-8): a reversão de conta/parcela/pagamento
/// roda SEMPRE; o caixa só é tocado quando o movimento do pagamento está num dia ABERTO (soft-mark
/// in-place). Num dia FECHADO o caixa NÃO é tocado (período congelado) — a reversão fica só no razão.
/// </summary>
public class EstornarPagamentoParcelaReceberUseCaseTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Categoria = Guid.NewGuid();

    private sealed record Cenario(
        EstornarPagamentoParcelaReceberUseCase Uc,
        ICaixaRepository CaixaRepo,
        ParcelaReceber Parcela,
        PagamentoParcela Pag,
        MovimentoCaixa? Mov);

    private static Cenario Montar(bool comMovimentoCaixa = true)
    {
        var conta = ContaReceber.Criar(Empresa, clienteId: null, Categoria, "Cliente", DateTime.UtcNow);
        conta.AdicionarParcela(1, 100m, DateTime.UtcNow.AddDays(30));
        conta.Emitir();
        var parcela = conta.Parcelas.First();
        parcela.ContaReceber = conta;

        var pag = PagamentoParcela.CriarConfirmado(Empresa, TipoLadoFinanceiro.Receber, 100m, "pix", DateTime.UtcNow);
        parcela.RegistrarPagamento(pag);
        conta.AtualizarStatusPorParcelas(); // -> Paga

        MovimentoCaixa? mov = null;
        if (comMovimentoCaixa)
        {
            mov = MovimentoCaixa.Criar(Empresa, "entrada", 100m, DateTime.UtcNow, lojaId: null);
            pag.AssociarMovimentoCaixa(mov.Id);
        }

        var contaRepo = Substitute.For<IContaReceberRepository>();
        contaRepo.GetParcelaWithContaAsync(Empresa, parcela.Id, Arg.Any<CancellationToken>()).Returns(parcela);
        var caixaRepo = Substitute.For<ICaixaRepository>();
        if (mov is not null)
            caixaRepo.GetMovimentoAsync(Empresa, mov.Id).Returns(mov);
        var uow = Substitute.For<IUnitOfWork>();
        var logger = Substitute.For<ILogger<EstornarPagamentoParcelaReceberUseCase>>();
        var uc = new EstornarPagamentoParcelaReceberUseCase(contaRepo, caixaRepo, uow, logger);
        return new Cenario(uc, caixaRepo, parcela, pag, mov);
    }

    private static EstornarPagamentoParcelaReceberCommand Cmd(Cenario c) =>
        new(Empresa, c.Parcela.Id, c.Pag.Id, "Lancado por engano", UserId: Guid.NewGuid(), UserNome: "Gerente");

    [Fact]
    public async Task Dia_aberto_reverte_razao_e_soft_marca_o_movimento()
    {
        var c = Montar();
        c.CaixaRepo.GetFechamentoDoDiaAsync(Empresa, Arg.Any<DateOnly>(), Arg.Any<Guid?>())
            .Returns((FechamentoCaixa?)null); // dia aberto

        var ok = await c.Uc.ExecuteAsync(Cmd(c));

        ok.Should().BeTrue();
        c.Pag.Status.Should().Be(StatusPagamentoParcela.Estornado);
        c.Mov!.EstornadoEm.Should().NotBeNull();           // movimento soft-marcado in-place
        c.Parcela.Status.Should().NotBe(StatusParcela.Paga); // razao revertido
        c.Parcela.ValorPago.Should().Be(0m);
    }

    [Fact]
    public async Task Dia_fechado_reverte_razao_mas_NAO_toca_o_caixa()
    {
        var c = Montar();
        c.CaixaRepo.GetFechamentoDoDiaAsync(Empresa, Arg.Any<DateOnly>(), Arg.Any<Guid?>())
            .Returns(FechamentoCaixa.Criar(Empresa, DateOnly.FromDateTime(DateTime.UtcNow), 0m, 0m, 0m, 0m, 0m, null)); // fechado

        var ok = await c.Uc.ExecuteAsync(Cmd(c));

        ok.Should().BeTrue();
        c.Pag.Status.Should().Be(StatusPagamentoParcela.Estornado); // razao revertido
        c.Parcela.Status.Should().NotBe(StatusParcela.Paga);
        c.Parcela.ValorPago.Should().Be(0m);
        c.Mov!.EstornadoEm.Should().BeNull();                       // periodo congelado: caixa intocado
        await c.CaixaRepo.DidNotReceive().UpdateMovimentoAsync(Arg.Any<MovimentoCaixa>());
    }

    [Fact]
    public async Task Retry_de_pagamento_ja_estornado_e_idempotente()
    {
        var c = Montar();
        c.CaixaRepo.GetFechamentoDoDiaAsync(Empresa, Arg.Any<DateOnly>(), Arg.Any<Guid?>())
            .Returns((FechamentoCaixa?)null);

        (await c.Uc.ExecuteAsync(Cmd(c))).Should().BeTrue();
        c.Pag.Status.Should().Be(StatusPagamentoParcela.Estornado);
        c.CaixaRepo.ClearReceivedCalls();

        var ok2 = await c.Uc.ExecuteAsync(Cmd(c)); // retry
        ok2.Should().BeTrue();
        await c.CaixaRepo.DidNotReceive().UpdateMovimentoAsync(Arg.Any<MovimentoCaixa>());
    }

    [Fact]
    public async Task Pagamento_sem_movimento_de_caixa_reverte_so_o_razao_sem_NRE()
    {
        var c = Montar(comMovimentoCaixa: false);

        var ok = await c.Uc.ExecuteAsync(Cmd(c));

        ok.Should().BeTrue();
        c.Pag.Status.Should().Be(StatusPagamentoParcela.Estornado);
        c.Parcela.Status.Should().NotBe(StatusParcela.Paga);
        await c.CaixaRepo.DidNotReceive().GetMovimentoAsync(Arg.Any<Guid>(), Arg.Any<Guid>());
    }
}

using EasyStock.Application.Common;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Caixa;
using EasyStock.Application.UseCases.FecharCaixa;
using EasyStock.Application.UseCases.ObterCaixaDia;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases.Caixa;

/// <summary>
/// F14 (#1244): o que o console precisa do caixa, sem mudar nada para quem já usa (PWA, Web,
/// Dashboard). Contado e diferença no fechamento, saldo do atendimento e resumo por método são
/// campos ADITIVOS; o saldo esperado de sempre fica igual.
/// </summary>
public class CaixaConsoleTests
{
    private readonly ICaixaRepository _repo = Substitute.For<ICaixaRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly DateOnly _hoje = HorarioBrasil.Hoje();

    private FecharCaixaUseCase Fechar() => new(_repo, _uow, Substitute.For<ILogger<FecharCaixaUseCase>>());

    // Dia aberto hoje: abertura 100, entradas 80, saída 20, pagamentos de pedido 120 e vendas do PDV 500.
    // Saldo esperado (de sempre) = 780; saldo do atendimento (sem as vendas do PDV) = 280.
    private void DiaAbertoComMovimento()
    {
        var movimentos = new[]
        {
            Movimento("abertura", 100m, null),
            Movimento("entrada", 50m, "dinheiro"),
            Movimento("entrada", 30m, "pix"),
            Movimento("saida", 20m, "dinheiro"),
        };
        _repo.GetAberturaPendenteAsync(_empresaId, null).Returns(movimentos[0]);
        _repo.GetFechamentoDoDiaAsync(_empresaId, _hoje, null).Returns((FechamentoCaixa?)null);
        _repo.GetMovimentosDoDiaAsync(_empresaId, _hoje, null).Returns(movimentos);
        _repo.GetTotalVendasDoDiaAsync(_empresaId, _hoje, null).Returns(500m);
        _repo.GetTotalPagamentosPedidosDoDiaAsync(_empresaId, _hoje, null).Returns(120m);
    }

    private MovimentoCaixa Movimento(string tipo, decimal valor, string? metodo)
    {
        var m = MovimentoCaixa.Criar(_empresaId, tipo, valor);
        m.Metodo = metodo;
        return m;
    }

    [Theory]
    [InlineData(330, 50)]   // sobrou dinheiro
    [InlineData(280, 0)]    // bateu certo
    [InlineData(200, -80)]  // faltou dinheiro
    public async Task FecharCaixa_ComContado_GravaContadoEDiferencaContraOSaldoDoAtendimento(decimal contado, decimal diferenca)
    {
        DiaAbertoComMovimento();
        FechamentoCaixa? gravado = null;
        await _repo.AddFechamentoAsync(Arg.Do<FechamentoCaixa>(f => gravado = f));

        var r = await Fechar().ExecuteAsync(new FecharCaixaCommand(_empresaId, ValorContado: contado));

        r.SaldoFinal.Should().Be(780m, "o saldo final de sempre não muda");
        r.ValorContado.Should().Be(contado);
        r.Diferenca.Should().Be(diferenca);
        gravado!.ValorContado.Should().Be(contado);
        gravado.Diferenca.Should().Be(diferenca);
    }

    [Fact]
    public async Task FecharCaixa_SemContado_FechaComoHoje()
    {
        DiaAbertoComMovimento();

        var r = await Fechar().ExecuteAsync(new FecharCaixaCommand(_empresaId));

        r.SaldoFinal.Should().Be(780m);
        r.ValorContado.Should().BeNull();
        r.Diferenca.Should().BeNull();
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task FecharCaixa_ContadoNegativo_Recusa()
    {
        DiaAbertoComMovimento();

        var act = () => Fechar().ExecuteAsync(new FecharCaixaCommand(_empresaId, ValorContado: -1m));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*contado*");
        await _repo.DidNotReceive().AddFechamentoAsync(Arg.Any<FechamentoCaixa>());
    }

    [Fact]
    public async Task ObterCaixaDia_SaldoEsperadoAntigoIgual_ESaldoDoAtendimentoSemVendasDoPdv()
    {
        DiaAbertoComMovimento();

        var r = await new ObterCaixaDiaUseCase(new CaixaSaldoCalculator(_repo))
            .ExecuteAsync(new ObterCaixaDiaQuery(_empresaId, _hoje));

        // Contrato que a PWA e o Web leem: igual ao de antes.
        r.SaldoEsperado.Should().Be(780m);
        r.TotalVendas.Should().Be(500m);
        // Novo, só o console lê: inicial + pagamentos de pedido + entradas − saídas.
        r.SaldoAtendimento.Should().Be(280m);
    }

    [Fact]
    public async Task ObterCaixaDia_ResumoPorMetodo_SomaPagamentosEEntradasEDescontaSaidas()
    {
        DiaAbertoComMovimento();
        var (ini, fim) = HorarioBrasil.JanelaDiaUtc(_hoje);
        var pedidoId = Guid.NewGuid();
        _repo.GetVendasNoIntervaloAsync(_empresaId, ini, fim, null).Returns(Array.Empty<Venda>());
        _repo.GetPagamentosPedidosListaNoIntervaloAsync(_empresaId, ini, fim, null).Returns(new[]
        {
            new PedidoPagamento { Id = Guid.NewGuid(), PedidoId = pedidoId, Metodo = "pix", Valor = 70m, PagoEm = ini.AddHours(13) },
            new PedidoPagamento { Id = Guid.NewGuid(), PedidoId = Guid.NewGuid(), Metodo = "dinheiro", Valor = 50m, PagoEm = ini.AddHours(14) },
        });

        var r = await new ObterCaixaDiaUseCase(new CaixaSaldoCalculator(_repo))
            .ExecuteAsync(new ObterCaixaDiaQuery(_empresaId, _hoje));

        // pix: 70 do pedido + 30 de entrada; dinheiro: 50 do pedido + 50 de entrada − 20 de saída.
        r.PorMetodo!.Should().BeEquivalentTo(new[]
        {
            new CaixaMetodoResult("pix", 100m),
            new CaixaMetodoResult("dinheiro", 80m),
        }, o => o.WithStrictOrdering());
        r.PorMetodo!.Sum(m => m.Valor).Should().Be(r.SaldoAtendimento - r.SaldoInicial);
        r.LinhasExtras!.Single(l => l.Valor == 70m).PedidoId.Should().Be(pedidoId);
    }
}

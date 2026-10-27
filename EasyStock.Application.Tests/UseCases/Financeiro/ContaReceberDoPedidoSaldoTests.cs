using EasyStock.Application.Common;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Application.Services.Pedidos;
using EasyStock.Application.UseCases.Financeiro.ContasReceber;
using EasyStock.Application.UseCases.Financeiro.Integracao;
using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Enums.Financeiro;
using EasyStock.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases.Financeiro;

/// <summary>
/// #2095 (F10): a conta a receber gerada do pedido cobra o que falta receber, não o total; pedido já pago não gera
/// conta; o vencimento é um dia civil de Brasília.
/// </summary>
public class ContaReceberDoPedidoSaldoTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Loja = Guid.NewGuid();

    private static (GerarContaReceberDePedidoUseCase Sut, Func<ContaReceber?> Gravada) Montar(SaldoFinanceiroPedido? saldos = null)
    {
        var contas = Substitute.For<IContaReceberRepository>();
        var categorias = Substitute.For<ICategoriaFinanceiraRepository>();
        var config = Substitute.For<IConfiguracaoLojaRepository>();
        var categoria = CategoriaFinanceira.Criar(Empresa, "Vendas", TipoCategoriaFinanceira.Receita);
        categorias.ListarAsync(Empresa, true, TipoCategoriaFinanceira.Receita, Arg.Any<CancellationToken>()).Returns([categoria]);
        categorias.GetByIdAsync(Empresa, categoria.Id, Arg.Any<CancellationToken>()).Returns(categoria);
        var cfg = ConfiguracaoLoja.CriarPadrao(Loja);
        cfg.GerarContaReceberAutomaticaDePedido = true;
        config.GetByLojaIdAsync(Loja).Returns(cfg);
        ContaReceber? gravada = null;
        contas.When(r => r.AddAsync(Arg.Any<ContaReceber>(), Arg.Any<CancellationToken>())).Do(ci => gravada = ci.Arg<ContaReceber>());
        var criar = new CriarContaReceberUseCase(contas, categorias, Substitute.For<ICentroCustoRepository>(),
            Substitute.For<IUnitOfWork>(), Substitute.For<ILogger<CriarContaReceberUseCase>>());
        var sut = new GerarContaReceberDePedidoUseCase(contas, categorias, config, criar,
            Substitute.For<ILogger<GerarContaReceberDePedidoUseCase>>(), saldos);
        return (sut, () => gravada);
    }

    private static Pedido PedidoDe(decimal total, decimal pago)
    {
        var pedido = Pedido.Criar(Empresa, lojaId: Loja);
        pedido.Total = Dinheiro.FromDecimal(total);
        if (pago > 0m)
            pedido.Pagamentos.Add(new PedidoPagamento { Id = Guid.NewGuid(), PedidoId = pedido.Id, Metodo = "pix", Valor = pago, PagoEm = DateTime.UtcNow });
        return pedido;
    }

    [Fact]
    public async Task Pedido_ja_pago_nao_gera_conta_a_receber()
    {
        var (sut, gravada) = Montar();

        var r = await sut.ExecuteAsync(new GerarContaReceberDePedidoCommand(Empresa, PedidoDe(120m, 120m)));

        r.Should().BeNull();
        gravada().Should().BeNull();
    }

    [Fact]
    public async Task Com_o_saldo_financeiro_do_pedido_o_estorno_manual_volta_a_ser_cobrado()
    {
        var pedidos = Substitute.For<IPedidoRepository>();
        var pedido = PedidoDe(50m, 50m);
        // R$ 20 devolvidos à mão: dos R$ 50 pagos, ficaram 30; faltam 20.
        pedidos.ListarEstornosManuaisAsync(Empresa, pedido.Id, Arg.Any<CancellationToken>())
            .Returns([new PedidoEstornoManual { Id = Guid.NewGuid(), EmpresaId = Empresa, PedidoId = pedido.Id, Valor = 20m }]);
        var saldos = new SaldoFinanceiroPedido(pedidos, Substitute.For<IEstornoOnlineRepository>(),
            Substitute.For<ICreditoClienteComandaRepository>());
        var (sut, gravada) = Montar(saldos);

        await sut.ExecuteAsync(new GerarContaReceberDePedidoCommand(Empresa, pedido));

        gravada()!.ValorTotal.Should().Be(20m);
    }

    [Fact]
    public async Task Pedido_com_sinal_gera_conta_so_do_saldo()
    {
        var (sut, gravada) = Montar();

        var r = await sut.ExecuteAsync(new GerarContaReceberDePedidoCommand(Empresa, PedidoDe(50m, 20m)));

        r.Should().NotBeNull();
        gravada()!.ValorTotal.Should().Be(30m);
    }

    [Fact]
    public async Task Vencimento_e_o_dia_civil_de_Brasilia_daqui_a_7_dias()
    {
        var (sut, gravada) = Montar();

        await sut.ExecuteAsync(new GerarContaReceberDePedidoCommand(Empresa, PedidoDe(50m, 0m)));

        gravada()!.Parcelas.Single().DataVencimento.Should().Be(HorarioBrasil.CivilComoInstanteUtc(HorarioBrasil.Hoje().AddDays(7)));
    }
}

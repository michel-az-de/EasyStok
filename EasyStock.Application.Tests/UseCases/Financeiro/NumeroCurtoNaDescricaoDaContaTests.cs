using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Financeiro.ContasPagar;
using EasyStock.Application.UseCases.Financeiro.ContasReceber;
using EasyStock.Application.UseCases.Financeiro.Integracao;
using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Enums.Financeiro;
using EasyStock.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases.Financeiro;

/// <summary>
/// #2018: o número curto do pedido na descrição da conta a receber/pagar é o mesmo do canhoto, da cozinha e do aviso
/// (8 caracteres do id, em maiúsculas). Antes o financeiro gravava minúsculo ("a1b2c3d4") para o mesmo pedido.
/// </summary>
public class NumeroCurtoNaDescricaoDaContaTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Loja = Guid.NewGuid();

    private static ConfiguracaoLoja ConfigLigada()
    {
        var config = ConfiguracaoLoja.CriarPadrao(Loja);
        config.GerarContaReceberAutomaticaDePedido = true;
        config.GerarContaPagarAutomaticaDePedidoFornecedor = true;
        return config;
    }

    [Fact]
    public async Task ContaReceberDoPedido_DescreveComONumeroCurtoDoPedido()
    {
        var contas = Substitute.For<IContaReceberRepository>();
        var categorias = Substitute.For<ICategoriaFinanceiraRepository>();
        var config = Substitute.For<IConfiguracaoLojaRepository>();
        var categoria = CategoriaFinanceira.Criar(Empresa, "Vendas", TipoCategoriaFinanceira.Receita);
        categorias.ListarAsync(Empresa, true, TipoCategoriaFinanceira.Receita, Arg.Any<CancellationToken>()).Returns([categoria]);
        categorias.GetByIdAsync(Empresa, categoria.Id, Arg.Any<CancellationToken>()).Returns(categoria);
        config.GetByLojaIdAsync(Loja).Returns(ConfigLigada());
        ContaReceber? gravada = null;
        contas.When(r => r.AddAsync(Arg.Any<ContaReceber>(), Arg.Any<CancellationToken>())).Do(ci => gravada = ci.Arg<ContaReceber>());
        var criar = new CriarContaReceberUseCase(contas, categorias, Substitute.For<ICentroCustoRepository>(),
            Substitute.For<IUnitOfWork>(), Substitute.For<ILogger<CriarContaReceberUseCase>>());
        var sut = new GerarContaReceberDePedidoUseCase(contas, categorias, config, criar,
            Substitute.For<ILogger<GerarContaReceberDePedidoUseCase>>());
        var pedido = Pedido.Criar(Empresa, lojaId: Loja);
        pedido.ClienteNome = "Ana";
        pedido.Total = Dinheiro.FromDecimal(50m);

        await sut.ExecuteAsync(new GerarContaReceberDePedidoCommand(Empresa, pedido));

        gravada.Should().NotBeNull();
        gravada!.Descricao.Should().Be($"Pedido {pedido.Id.ToString("N")[..8].ToUpperInvariant()} — Ana");
    }

    [Fact]
    public async Task ContaPagarDoPedidoFornecedor_DescreveComONumeroCurtoDoPedido()
    {
        var contas = Substitute.For<IContaPagarRepository>();
        var categorias = Substitute.For<ICategoriaFinanceiraRepository>();
        var config = Substitute.For<IConfiguracaoLojaRepository>();
        var categoria = CategoriaFinanceira.Criar(Empresa, "Compras", TipoCategoriaFinanceira.Despesa);
        categorias.ListarAsync(Empresa, true, TipoCategoriaFinanceira.Despesa, Arg.Any<CancellationToken>()).Returns([categoria]);
        categorias.GetByIdAsync(Empresa, categoria.Id, Arg.Any<CancellationToken>()).Returns(categoria);
        config.GetByLojaIdAsync(Loja).Returns(ConfigLigada());
        ContaPagar? gravada = null;
        contas.When(r => r.AddAsync(Arg.Any<ContaPagar>(), Arg.Any<CancellationToken>())).Do(ci => gravada = ci.Arg<ContaPagar>());
        var criar = new CriarContaPagarUseCase(contas, categorias, Substitute.For<ICentroCustoRepository>(),
            Substitute.For<IUnitOfWork>(), Substitute.For<ILogger<CriarContaPagarUseCase>>());
        var sut = new GerarContaPagarDePedidoFornecedorUseCase(contas, categorias, config, criar,
            Substitute.For<ILogger<GerarContaPagarDePedidoFornecedorUseCase>>());
        var compra = new PedidoFornecedor { Id = Guid.NewGuid(), EmpresaId = Empresa, FornecedorId = Guid.NewGuid(), LojaId = Loja, ValorEstimado = 80m };

        await sut.ExecuteAsync(new GerarContaPagarDePedidoFornecedorCommand(Empresa, compra));

        gravada.Should().NotBeNull();
        gravada!.Descricao.Should().Be($"Compra {compra.Id.ToString("N")[..8].ToUpperInvariant()}");
    }
}

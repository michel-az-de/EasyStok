using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.PreviewSugestaoCompra;
using EasyStock.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases.PreviewSugestaoCompra;

/// <summary>
/// #2441: o insumo cadastrado pelo preço de compra guarda o custo em 6 casas (#2363), mas o lote do saldo inicial
/// nasce com 2 casas. A prévia de compra tem de usar o custo da ficha, como o cálculo da produção, senão a farinha
/// de R$ 0,0045/g sai a R$ 0,00.
/// </summary>
public class PreviewSugestaoCompraUseCaseTests
{
    private readonly Guid _empresa = Guid.NewGuid();
    private readonly IProdutoRepository _produtos = Substitute.For<IProdutoRepository>();
    private readonly IItemEstoqueRepository _estoque = Substitute.For<IItemEstoqueRepository>();
    private readonly IFornecedorRepository _fornecedores = Substitute.For<IFornecedorRepository>();

    private PreviewSugestaoCompraUseCase Build() => new(
        _produtos, _estoque, _fornecedores, Substitute.For<ILogger<PreviewSugestaoCompraUseCase>>());

    private Produto Insumo(decimal? custoDaFicha, bool ehInsumo = true)
    {
        var p = new Produto { Id = Guid.NewGuid(), EmpresaId = _empresa, Nome = "Farinha", EhInsumo = ehInsumo };
        if (ehInsumo) p.DefinirCustoDoInsumo(custoDaFicha);
        else if (custoDaFicha is { } c) p.CustoReferencia = Dinheiro.FromDecimal(c);
        _produtos.GetByIdAsync(_empresa, p.Id).Returns(p);
        return p;
    }

    private void Lote(Produto p, decimal custo)
    {
        IReadOnlyCollection<ItemEstoque> lotes =
            [new ItemEstoque { Id = Guid.NewGuid(), EmpresaId = _empresa, ProdutoId = p.Id, CustoUnitario = Dinheiro.FromDecimal(custo) }];
        _estoque.GetByProdutosAsync(_empresa, Arg.Any<IEnumerable<Guid>>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>(), Arg.Any<bool>())
            .Returns(new Dictionary<Guid, IReadOnlyCollection<ItemEstoque>> { [p.Id] = lotes });
    }

    [Fact]
    public async Task Insumo_usa_o_custo_de_6_casas_e_nao_o_lote_arredondado()
    {
        var farinha = Insumo(0.0045m);
        Lote(farinha, 0.00m);

        var r = await Build().ExecuteAsync(new PreviewSugestaoCompraCommand(
            _empresa, null, [new InsumoFaltanteInput(farinha.Id, 5000m, UnidadeMedida.G)]));

        var linha = r.PorFornecedor.SelectMany(g => g.Linhas).Single();
        linha.CustoUnitarioReferencia.Should().Be(0.0045m);
        linha.Subtotal.Should().Be(22.50m);
    }

    [Fact]
    public async Task Produto_que_nao_e_insumo_segue_pelo_custo_do_lote()
    {
        var produto = Insumo(9.99m, ehInsumo: false);
        Lote(produto, 12.00m);

        var r = await Build().ExecuteAsync(new PreviewSugestaoCompraCommand(
            _empresa, null, [new InsumoFaltanteInput(produto.Id, 2m, UnidadeMedida.Un)]));

        r.PorFornecedor.SelectMany(g => g.Linhas).Single().Subtotal.Should().Be(24.00m);
    }
}

using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Atendimento.Producao;
using EasyStock.Application.UseCases.CadastrarProduto;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Producao;

/// <summary>M2.3 (#1496): insumos com saldo, mínimo, custo e onde são usados; abaixo do mínimo vai para "Comprar".</summary>
public class InsumosDaProducaoUseCaseTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private readonly IProdutoRepository _produtos = Substitute.For<IProdutoRepository>();
    private readonly IProdutoComposicaoRepository _composicao = Substitute.For<IProdutoComposicaoRepository>();
    private readonly IItemEstoqueRepository _estoque = Substitute.For<IItemEstoqueRepository>();
    private readonly ICategoriaRepository _categorias = Substitute.For<ICategoriaRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly List<Categoria> _categoriasGravadas = [];
    private readonly Dictionary<Guid, Produto> _gravados = [];

    public InsumosDaProducaoUseCaseTests()
    {
        _categorias.GetByEmpresaAsync(EmpresaId).Returns(_ => _categoriasGravadas.ToList());
        _categorias.When(c => c.AddAsync(Arg.Any<Categoria>())).Do(c => _categoriasGravadas.Add(c.Arg<Categoria>()));
        _categorias.GetByIdAsync(EmpresaId, Arg.Any<Guid>()).Returns(c => _categoriasGravadas.FirstOrDefault(x => x.Id == c.ArgAt<Guid>(1)));
        _categorias.GetByIdAsync(Arg.Any<Guid>()).Returns(c => _categoriasGravadas.FirstOrDefault(x => x.Id == c.Arg<Guid>()));
        _produtos.When(r => r.InsertAsync(Arg.Any<Produto>())).Do(c => _gravados[c.Arg<Produto>().Id] = c.Arg<Produto>());
        _produtos.GetByIdAsync(EmpresaId, Arg.Any<Guid>()).Returns(c => _gravados.GetValueOrDefault(c.ArgAt<Guid>(1)));
    }

    private InsumosDaProducaoUseCase Sut() => new(_produtos, _composicao, _estoque, _categorias,
        new CadastrarProdutoUseCase(_produtos, _categorias, Substitute.For<IProdutoCaracteristicaRepository>(),
            Substitute.For<IProdutoEmbalagemRepository>(), Substitute.For<IProdutoVariacaoRepository>(), _uow,
            Substitute.For<ILogger<CadastrarProdutoUseCase>>()),
        _uow);

    private static Produto Insumo(string nome, int? minimo, UnidadeMedida unidade = UnidadeMedida.G) => new()
    {
        Id = Guid.NewGuid(), EmpresaId = EmpresaId, Nome = nome, EhInsumo = true, Status = StatusProduto.Ativo,
        QuantidadeMinima = minimo, UnidadeMedidaBase = unidade,
    };

    [Fact]
    public async Task Listar_ComSaldoMinimoEReceitas_EAbaixoDoMinimoMarcado()
    {
        var molho = Insumo("Molho sugo", 2000);
        var bandeja = Insumo("Bandeja 500 ml", 50, UnidadeMedida.Un);
        _produtos.GetInsumosAsync(EmpresaId, Arg.Any<CancellationToken>()).Returns([molho, bandeja]);
        _estoque.GetSaldoDisponivelPorProdutosAsync(EmpresaId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, decimal> { [molho.Id] = 1500, [bandeja.Id] = 80 });
        _composicao.ContarReceitasPorInsumoAsync(EmpresaId, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, int> { [molho.Id] = 3 });

        var lista = await Sut().ListarAsync(EmpresaId);

        lista.Select(i => (i.Nome, i.Saldo, i.Receitas, i.AbaixoDoMinimo)).Should().Equal(
            ("Molho sugo", 1500m, 3, true), ("Bandeja 500 ml", 80m, 0, false));
    }

    [Fact]
    public async Task Criar_MarcaComoInsumo_NaCategoriaInsumos_ComUnidadeEMinimo()
    {
        var id = await Sut().CriarAsync(EmpresaId, Guid.NewGuid(), new InsumoInput("Recheio de ricota", UnidadeMedida.G, 1000, 0.05m));

        var p = _gravados[id];
        p.EhInsumo.Should().BeTrue();
        p.UnidadeMedidaBase.Should().Be(UnidadeMedida.G);
        p.QuantidadeMinima.Should().Be(1000);
        _categoriasGravadas.Should().ContainSingle(c => c.Nome == InsumosDaProducaoUseCase.CategoriaInsumos);
    }

    [Fact]
    public async Task Atualizar_ProdutoQueNaoEInsumo_400()
    {
        var prato = new Produto { Id = Guid.NewGuid(), EmpresaId = EmpresaId, Nome = "Lasanha", EhInsumo = false };
        _gravados[prato.Id] = prato;

        var act = () => Sut().AtualizarAsync(EmpresaId, prato.Id, new InsumoInput(null, null, 5, null));

        await act.Should().ThrowAsync<UseCaseValidationException>();
        prato.QuantidadeMinima.Should().BeNull();
    }
}

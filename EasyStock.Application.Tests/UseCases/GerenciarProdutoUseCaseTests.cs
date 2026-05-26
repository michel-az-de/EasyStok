using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.TestHelpers;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.GerenciarProduto;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using FluentAssertions;
using NSubstitute;

namespace EasyStock.Application.Tests.UseCases;

public class GerenciarProdutoUseCaseTests
{
    private readonly IProdutoRepository _produtoRepository = Substitute.For<IProdutoRepository>();
    private readonly ICategoriaRepository _categoriaRepository = Substitute.For<ICategoriaRepository>();
    private readonly IProdutoVariacaoRepository _variacaoRepository = Substitute.For<IProdutoVariacaoRepository>();
    private readonly IProdutoCaracteristicaRepository _caracteristicaRepository = Substitute.For<IProdutoCaracteristicaRepository>();
    private readonly IProdutoEmbalagemRepository _embalagemRepository = Substitute.For<IProdutoEmbalagemRepository>();
    private readonly IItemEstoqueRepository _itemEstoqueRepository = Substitute.For<IItemEstoqueRepository>();
    private readonly IMovimentacaoEstoqueRepository _movimentacaoRepository = Substitute.For<IMovimentacaoEstoqueRepository>();
    private readonly FakeUnitOfWork _unitOfWork = new();

    private GerenciarProdutoUseCase CriarUseCase() => new(
        _produtoRepository,
        _categoriaRepository,
        _variacaoRepository,
        _caracteristicaRepository,
        _embalagemRepository,
        _itemEstoqueRepository,
        _movimentacaoRepository,
        _unitOfWork);

    [Fact]
    public async Task Deve_falhar_ao_remover_produto_com_estoque()
    {
        var useCase = CriarUseCase();
        var empresaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();

        _produtoRepository.GetByIdAsync(empresaId, produtoId).Returns(new Produto
        {
            Id = produtoId,
            EmpresaId = empresaId,
            Nome = "Produto",
            Status = StatusProduto.Ativo
        });
        _itemEstoqueRepository.ExisteEstoqueDoProdutoAsync(empresaId, produtoId).Returns(true);

        var act = () => useCase.RemoverAsync(empresaId, produtoId);

        await act.Should().ThrowAsync<UseCaseValidationException>()
            .WithMessage("*estoque disponivel*");
        await _produtoRepository.DidNotReceive().UpdateAsync(Arg.Any<Produto>());
    }

    [Fact]
    public async Task Deve_atualizar_produto_com_caracteristicas_e_embalagens()
    {
        var useCase = CriarUseCase();
        var empresaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();

        _produtoRepository.GetByIdAsync(empresaId, produtoId).Returns(new Produto
        {
            Id = produtoId,
            EmpresaId = empresaId,
            CategoriaId = categoriaId,
            Nome = "Produto Original",
            Status = StatusProduto.Ativo
        });
        _categoriaRepository.GetByIdAsync(empresaId, categoriaId).Returns(new Categoria
        {
            Id = categoriaId,
            EmpresaId = empresaId,
            Nome = "Categoria"
        });

        var command = new AtualizarProdutoCommand(
            empresaId, produtoId, categoriaId, null, "Produto Atualizado",
            null, null, TipoProduto.Fisico, null, null, false, null,
            null, null, null, null, StatusProduto.Ativo,
            new[] { new ProdutoCaracteristicaInput("Material", "Algodao", null, null, 0) },
            new[] { new ProdutoEmbalagemInput("Caixa", null, null, true) },
            null);

        await useCase.AtualizarAsync(command);

        // Old ones batch-deleted by produto
        await _caracteristicaRepository.Received(1).DeleteByProdutoAsync(empresaId, produtoId);
        await _embalagemRepository.Received(1).DeleteByProdutoAsync(empresaId, produtoId);

        // New ones inserted
        await _caracteristicaRepository.Received(1).InsertAsync(Arg.Is<ProdutoCaracteristica>(c => c.Nome == "Material"));
        await _embalagemRepository.Received(1).InsertAsync(Arg.Is<ProdutoEmbalagem>(e => e.Nome == "Caixa" && e.Padrao));

        _unitOfWork.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task Deve_retornar_detalhe_com_caracteristicas_embalagens_e_dimensoes()
    {
        var useCase = CriarUseCase();
        var empresaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();

        var carac = new ProdutoCaracteristica { Id = Guid.NewGuid(), EmpresaId = empresaId, ProdutoId = produtoId, Nome = "Material", Descricao = "Algodao", OrdemExibicao = 0 };
        var emb = new ProdutoEmbalagem
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = produtoId,
            Nome = "Caixa",
            Padrao = true,
            Dimensoes = Dimensoes.From(0.5m, 35m, 25m, 45m)
        };

        _produtoRepository.GetDetalheAsync(empresaId, produtoId).Returns(new Produto
        {
            Id = produtoId,
            EmpresaId = empresaId,
            CategoriaId = Guid.NewGuid(),
            Nome = "Produto Completo",
            Status = StatusProduto.Ativo,
            Dimensoes = Dimensoes.From(1.5m, 30m, 20m, 40m),
            CustoReferencia = Dinheiro.FromDecimal(50m),
            PrecoReferencia = Dinheiro.FromDecimal(100m),
            MargemEstimada = 50m,
            Caracteristicas = new[] { carac },
            Embalagens = new[] { emb }
        });
        _itemEstoqueRepository.GetByProdutoAsync(empresaId, produtoId).Returns([]);
        _variacaoRepository.GetByProdutoAsync(empresaId, produtoId).Returns([]);

        var result = await useCase.ObterDetalheAsync(empresaId, produtoId);

        result.Dimensoes.Should().NotBeNull();
        result.Dimensoes!.Peso.Should().Be(1.5m);
        result.Dimensoes.Largura.Should().Be(30m);
        result.Dimensoes.Altura.Should().Be(20m);
        result.Dimensoes.Comprimento.Should().Be(40m);

        result.Caracteristicas.Should().HaveCount(1);
        result.Caracteristicas.First().Nome.Should().Be("Material");

        result.Embalagens.Should().HaveCount(1);
        result.Embalagens.First().Nome.Should().Be("Caixa");
        result.Embalagens.First().Padrao.Should().BeTrue();
        result.Embalagens.First().Dimensoes.Should().NotBeNull();
    }

    [Fact]
    public async Task Deve_falhar_quando_mais_de_uma_embalagem_padrao_no_update()
    {
        var useCase = CriarUseCase();
        var empresaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();

        _produtoRepository.GetByIdAsync(empresaId, produtoId).Returns(new Produto
        {
            Id = produtoId,
            EmpresaId = empresaId,
            CategoriaId = categoriaId,
            Nome = "Produto",
            Status = StatusProduto.Ativo
        });
        _categoriaRepository.GetByIdAsync(empresaId, categoriaId).Returns(new Categoria
        {
            Id = categoriaId,
            EmpresaId = empresaId,
            Nome = "Categoria"
        });

        var command = new AtualizarProdutoCommand(
            empresaId, produtoId, categoriaId, null, "Produto",
            null, null, TipoProduto.Fisico, null, null, false, null,
            null, null, null, null, StatusProduto.Ativo,
            null,
            new[]
            {
                new ProdutoEmbalagemInput("Caixa 1", null, null, true),
                new ProdutoEmbalagemInput("Caixa 2", null, null, true)
            },
            null);

        var act = () => useCase.AtualizarAsync(command);

        await act.Should().ThrowAsync<UseCaseValidationException>()
            .WithMessage("*embalagem*padrao*");
    }

    [Fact]
    public async Task Atualizar_preserva_AtributosJson_quando_command_omite_campo()
    {
        // Regressao: ProdutoFormViewModel nao carrega AtributosJson. Sem o guard em
        // GerenciarProdutoUseCase, qualquer edicao via Form zerava a ficha tecnica
        // cadastrada via PUT /api/produtos/{id}/ficha-tecnica.
        var useCase = CriarUseCase();
        var empresaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        const string fichaJsonOriginal = """{"nutricional":{"kcal":250,"porcao_g":100}}""";

        _produtoRepository.GetByIdAsync(empresaId, produtoId).Returns(new Produto
        {
            Id = produtoId,
            EmpresaId = empresaId,
            CategoriaId = categoriaId,
            Nome = "Ravioli",
            Status = StatusProduto.Ativo,
            AtributosJson = fichaJsonOriginal
        });
        _categoriaRepository.GetByIdAsync(empresaId, categoriaId).Returns(new Categoria
        {
            Id = categoriaId,
            EmpresaId = empresaId,
            Nome = "Massas"
        });

        var command = new AtualizarProdutoCommand(
            empresaId, produtoId, categoriaId, null, "Ravioli atualizado",
            null, null, TipoProduto.Fisico, null, null, false, null,
            null, null, null, AtributosJson: null, StatusProduto.Ativo,
            null, null, null);

        await useCase.AtualizarAsync(command);

        await _produtoRepository.Received(1).UpdateAsync(Arg.Is<Produto>(p =>
            p.Id == produtoId && p.AtributosJson == fichaJsonOriginal));
    }
}

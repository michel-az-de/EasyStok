using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.CadastrarProduto;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Exceptions;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace EasyStock.Application.Tests.UseCases;

public class CadastrarProdutoUseCaseTests
{
    [Fact]
    public async Task Deve_cadastrar_produto_com_variacoes_caracteristicas_e_embalagens()
    {
        var produtoRepository = Substitute.For<IProdutoRepository>();
        var categoriaRepository = Substitute.For<ICategoriaRepository>();
        var caracteristicaRepository = Substitute.For<IProdutoCaracteristicaRepository>();
        var embalagemRepository = Substitute.For<IProdutoEmbalagemRepository>();
        var variacaoRepository = Substitute.For<IProdutoVariacaoRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var logger = Substitute.For<ILogger<CadastrarProdutoUseCase>>();

        var useCase = new CadastrarProdutoUseCase(
            produtoRepository,
            categoriaRepository,
            caracteristicaRepository,
            embalagemRepository,
            variacaoRepository,
            unitOfWork,
            logger);

        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        categoriaRepository.GetByIdAsync(categoriaId).Returns(new Categoria { Id = categoriaId, EmpresaId = empresaId, Nome = "Audio" });

        var command = new CadastrarProdutoCommand(
            empresaId,
            categoriaId,
            null,
            "Galaxy Buds FE",
            "Fone bluetooth",
            "Samsung",
            TipoProduto.Fisico,
            "BUDS-FE",
            "7890000000001",
            false,
            new DimensoesInput(0.3m, 10m, 5m, 8m),
            250m,
            399.90m,
            59.96m,
            "{\"cor\":\"grafite\"}",
            "[\"foto1.jpg\"]",
            [new ProdutoCaracteristicaInput("Cor", "Cor principal", null, "Grafite", 1)],
            [new ProdutoEmbalagemInput("Caixa", "Caixa padrao", new DimensoesInput(0.4m, 12m, 6m, 10m), true)],
            [new ProdutoVariacaoInput("Grafite", "Grafite", "Unico", "Buds FE Grafite", "BUDS-FE-GRAF", "7890000000002", null, null)]);

        var result = await useCase.ExecuteAsync(command);

        await produtoRepository.Received(1).InsertAsync(Arg.Is<Produto>(p =>
            p.Id == result.ProdutoId &&
            p.Nome == "Galaxy Buds FE" &&
            p.Status == StatusProduto.Ativo));

        await caracteristicaRepository.Received(1).InsertAsync(Arg.Is<ProdutoCaracteristica>(c =>
            c.ProdutoId == result.ProdutoId &&
            c.Nome == "Cor"));

        await embalagemRepository.Received(1).InsertAsync(Arg.Is<ProdutoEmbalagem>(e =>
            e.ProdutoId == result.ProdutoId &&
            e.Nome == "Caixa"));

        await variacaoRepository.Received(1).InsertAsync(Arg.Is<ProdutoVariacao>(v =>
            v.ProdutoId == result.ProdutoId &&
            v.Nome == "Grafite"));

        await unitOfWork.Received(1).CommitAsync();
    }

    [Fact]
    public async Task Deve_falhar_quando_sku_base_ja_existe_na_empresa()
    {
        var produtoRepository = Substitute.For<IProdutoRepository>();
        var categoriaRepository = Substitute.For<ICategoriaRepository>();
        var caracteristicaRepository = Substitute.For<IProdutoCaracteristicaRepository>();
        var embalagemRepository = Substitute.For<IProdutoEmbalagemRepository>();
        var variacaoRepository = Substitute.For<IProdutoVariacaoRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var logger = Substitute.For<ILogger<CadastrarProdutoUseCase>>();

        var useCase = new CadastrarProdutoUseCase(
            produtoRepository,
            categoriaRepository,
            caracteristicaRepository,
            embalagemRepository,
            variacaoRepository,
            unitOfWork,
            logger);

        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        categoriaRepository.GetByIdAsync(categoriaId).Returns(new Categoria { Id = categoriaId, EmpresaId = empresaId, Nome = "Audio" });
        produtoRepository.ExistsSkuBaseAsync(empresaId, "BUDS-FE", Arg.Any<Guid?>()).Returns(true);

        var command = new CadastrarProdutoCommand(
            empresaId,
            categoriaId,
            null,
            "Galaxy Buds FE",
            null,
            null,
            TipoProduto.Fisico,
            "BUDS-FE",
            null,
            false,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null);

        var act = () => useCase.ExecuteAsync(command);

        await act.Should().ThrowAsync<UseCaseValidationException>()
            .WithMessage("*SKU duplicado*");
    }

    [Fact]
    public async Task Deve_cadastrar_produto_com_subcategoria_valida()
    {
        var produtoRepository = Substitute.For<IProdutoRepository>();
        var categoriaRepository = Substitute.For<ICategoriaRepository>();
        var caracteristicaRepository = Substitute.For<IProdutoCaracteristicaRepository>();
        var embalagemRepository = Substitute.For<IProdutoEmbalagemRepository>();
        var variacaoRepository = Substitute.For<IProdutoVariacaoRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var logger = Substitute.For<ILogger<CadastrarProdutoUseCase>>();

        var useCase = new CadastrarProdutoUseCase(
            produtoRepository, categoriaRepository, caracteristicaRepository,
            embalagemRepository, variacaoRepository, unitOfWork, logger);

        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        var subcategoriaId = Guid.NewGuid();

        categoriaRepository.GetByIdAsync(categoriaId)
            .Returns(new Categoria { Id = categoriaId, EmpresaId = empresaId, Nome = "Eletronicos" });
        categoriaRepository.GetByIdAsync(subcategoriaId)
            .Returns(new Categoria { Id = subcategoriaId, EmpresaId = empresaId, Nome = "Fones", CategoriaPaiId = categoriaId });

        var command = new CadastrarProdutoCommand(
            empresaId, categoriaId, subcategoriaId, "Galaxy Buds",
            null, null, TipoProduto.Fisico, null, null, false,
            null, null, null, null, null, null, null, null, null);

        var result = await useCase.ExecuteAsync(command);

        await produtoRepository.Received(1).InsertAsync(Arg.Is<Produto>(p =>
            p.SubcategoriaId == subcategoriaId));
        await unitOfWork.Received(1).CommitAsync();
    }

    [Fact]
    public async Task Deve_falhar_quando_subcategoria_nao_pertence_a_categoria_informada()
    {
        var produtoRepository = Substitute.For<IProdutoRepository>();
        var categoriaRepository = Substitute.For<ICategoriaRepository>();
        var caracteristicaRepository = Substitute.For<IProdutoCaracteristicaRepository>();
        var embalagemRepository = Substitute.For<IProdutoEmbalagemRepository>();
        var variacaoRepository = Substitute.For<IProdutoVariacaoRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var logger = Substitute.For<ILogger<CadastrarProdutoUseCase>>();

        var useCase = new CadastrarProdutoUseCase(
            produtoRepository, categoriaRepository, caracteristicaRepository,
            embalagemRepository, variacaoRepository, unitOfWork, logger);

        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        var outraCategoriaId = Guid.NewGuid();
        var subcategoriaId = Guid.NewGuid();

        categoriaRepository.GetByIdAsync(categoriaId)
            .Returns(new Categoria { Id = categoriaId, EmpresaId = empresaId, Nome = "Eletronicos" });
        // subcategoria pertence a outra categoria
        categoriaRepository.GetByIdAsync(subcategoriaId)
            .Returns(new Categoria { Id = subcategoriaId, EmpresaId = empresaId, Nome = "Fones", CategoriaPaiId = outraCategoriaId });

        var command = new CadastrarProdutoCommand(
            empresaId, categoriaId, subcategoriaId, "Galaxy Buds",
            null, null, TipoProduto.Fisico, null, null, false,
            null, null, null, null, null, null, null, null, null);

        var act = () => useCase.ExecuteAsync(command);

        await act.Should().ThrowAsync<UseCaseValidationException>()
            .WithMessage("*subcategoria nao pertence a categoria*");
    }

    [Fact]
    public async Task Deve_cadastrar_caracteristica_com_variacao_id()
    {
        var produtoRepository = Substitute.For<IProdutoRepository>();
        var categoriaRepository = Substitute.For<ICategoriaRepository>();
        var caracteristicaRepository = Substitute.For<IProdutoCaracteristicaRepository>();
        var embalagemRepository = Substitute.For<IProdutoEmbalagemRepository>();
        var variacaoRepository = Substitute.For<IProdutoVariacaoRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var logger = Substitute.For<ILogger<CadastrarProdutoUseCase>>();

        var useCase = new CadastrarProdutoUseCase(
            produtoRepository, categoriaRepository, caracteristicaRepository,
            embalagemRepository, variacaoRepository, unitOfWork, logger);

        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        var variacaoId = Guid.NewGuid();

        categoriaRepository.GetByIdAsync(categoriaId)
            .Returns(new Categoria { Id = categoriaId, EmpresaId = empresaId, Nome = "Roupas" });

        var command = new CadastrarProdutoCommand(
            empresaId, categoriaId, null, "Camiseta",
            null, null, TipoProduto.Fisico, null, null, false,
            null, null, null, null, null, null,
            [new ProdutoCaracteristicaInput("Cor", "Cor da variacao", null, "Azul", 1, variacaoId)],
            null, null);

        var result = await useCase.ExecuteAsync(command);

        await caracteristicaRepository.Received(1).InsertAsync(Arg.Is<ProdutoCaracteristica>(c =>
            c.ProdutoId == result.ProdutoId &&
            c.VariacaoId == variacaoId));
    }
}

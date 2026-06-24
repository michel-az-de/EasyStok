using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.TestHelpers;
using EasyStock.Application.UseCases.CadastrarProduto;
using Microsoft.Extensions.Logging;

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
        var unitOfWork = new FakeUnitOfWork();
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
        categoriaRepository.GetByIdAsync(empresaId, categoriaId).Returns(new Categoria { Id = categoriaId, EmpresaId = empresaId, Nome = "Audio" });

        var command = new CadastrarProdutoCommand(
            empresaId,
            categoriaId,
            null,
            "Galaxy Buds FE",
            "Fone bluetooth",
            "Samsung",
            TipoProduto.Fisico,
            "BUDS-FE",
            "7890000000000",
            false,
            new DimensoesInput(0.3m, 10m, 5m, 8m),
            250m,
            399.90m,
            59.96m,
            "{\"cor\":\"grafite\"}",
            "[\"foto1.jpg\"]",
            [new ProdutoCaracteristicaInput("Cor", "Cor principal", null, "Grafite", 1)],
            [new ProdutoEmbalagemInput("Caixa", "Caixa padrao", new DimensoesInput(0.4m, 12m, 6m, 10m), true)],
            [new ProdutoVariacaoInput("Grafite", "Grafite", "Unico", "Buds FE Grafite", "BUDS-FE-GRAF", "7891000100103", null, null)]);

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

        unitOfWork.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task Deve_falhar_quando_sku_base_ja_existe_na_empresa()
    {
        var produtoRepository = Substitute.For<IProdutoRepository>();
        var categoriaRepository = Substitute.For<ICategoriaRepository>();
        var caracteristicaRepository = Substitute.For<IProdutoCaracteristicaRepository>();
        var embalagemRepository = Substitute.For<IProdutoEmbalagemRepository>();
        var variacaoRepository = Substitute.For<IProdutoVariacaoRepository>();
        var unitOfWork = new FakeUnitOfWork();
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
        categoriaRepository.GetByIdAsync(empresaId, categoriaId).Returns(new Categoria { Id = categoriaId, EmpresaId = empresaId, Nome = "Audio" });
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

    // ════════════════════════════════════════════════════════════════════
    // BUG-04 / BUG-02 (QA v1.10 #674): sanitizacao de tags HTML + teto de preco
    // ════════════════════════════════════════════════════════════════════

    private static (CadastrarProdutoUseCase uc, Guid empresaId, Guid categoriaId) SetupValido()
    {
        var produtoRepository = Substitute.For<IProdutoRepository>();
        var categoriaRepository = Substitute.For<ICategoriaRepository>();
        var caracteristicaRepository = Substitute.For<IProdutoCaracteristicaRepository>();
        var embalagemRepository = Substitute.For<IProdutoEmbalagemRepository>();
        var variacaoRepository = Substitute.For<IProdutoVariacaoRepository>();
        var logger = Substitute.For<ILogger<CadastrarProdutoUseCase>>();
        var uc = new CadastrarProdutoUseCase(produtoRepository, categoriaRepository,
            caracteristicaRepository, embalagemRepository, variacaoRepository, new FakeUnitOfWork(), logger);
        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        categoriaRepository.GetByIdAsync(empresaId, categoriaId)
            .Returns(new Categoria { Id = categoriaId, EmpresaId = empresaId, Nome = "Geral" });
        return (uc, empresaId, categoriaId);
    }

    private static CadastrarProdutoCommand CmdBase(Guid empresaId, Guid categoriaId) =>
        new(empresaId, categoriaId, null, "Produto OK", null, null, TipoProduto.Fisico,
            null, null, false, null, null, null, null, null, null, null, null, null);

    [Fact] // Characterization: Nome com <script> JA era rejeitado em master fresco -> achado do QA = dado stale.
    public async Task Deve_rejeitar_nome_com_tags_html()
    {
        var (uc, empresaId, categoriaId) = SetupValido();
        var cmd = CmdBase(empresaId, categoriaId) with { Nome = "<script>alert('xss')</script>" };
        var act = () => uc.ExecuteAsync(cmd);
        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*tags HTML*");
    }

    [Theory] // Guarda nova: DescricaoBase com payloads de bypass (img/svg/script) rejeitada na origem.
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("\"><svg onload=alert(1)>")]
    [InlineData("<script>alert(1)</script>")]
    public async Task Deve_rejeitar_descricao_com_tags_html(string payload)
    {
        var (uc, empresaId, categoriaId) = SetupValido();
        var cmd = CmdBase(empresaId, categoriaId) with { DescricaoBase = payload };
        var act = () => uc.ExecuteAsync(cmd);
        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*tags HTML*");
    }

    [Fact] // Characterization: preco acima do teto JA era rejeitado -> os R$142M do QA = dado stale.
    public async Task Deve_rejeitar_preco_acima_do_teto()
    {
        var (uc, empresaId, categoriaId) = SetupValido();
        var cmd = CmdBase(empresaId, categoriaId) with { PrecoReferencia = 142_857_142.84m };
        var act = () => uc.ExecuteAsync(cmd);
        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*máximo*");
    }

    [Fact]
    public async Task Deve_cadastrar_produto_com_subcategoria_valida()
    {
        var produtoRepository = Substitute.For<IProdutoRepository>();
        var categoriaRepository = Substitute.For<ICategoriaRepository>();
        var caracteristicaRepository = Substitute.For<IProdutoCaracteristicaRepository>();
        var embalagemRepository = Substitute.For<IProdutoEmbalagemRepository>();
        var variacaoRepository = Substitute.For<IProdutoVariacaoRepository>();
        var unitOfWork = new FakeUnitOfWork();
        var logger = Substitute.For<ILogger<CadastrarProdutoUseCase>>();

        var useCase = new CadastrarProdutoUseCase(
            produtoRepository, categoriaRepository, caracteristicaRepository,
            embalagemRepository, variacaoRepository, unitOfWork, logger);

        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        var subcategoriaId = Guid.NewGuid();

        categoriaRepository.GetByIdAsync(empresaId, categoriaId)
            .Returns(new Categoria { Id = categoriaId, EmpresaId = empresaId, Nome = "Eletronicos" });
        categoriaRepository.GetByIdAsync(empresaId, subcategoriaId)
            .Returns(new Categoria { Id = subcategoriaId, EmpresaId = empresaId, Nome = "Fones", CategoriaPaiId = categoriaId });

        var command = new CadastrarProdutoCommand(
            empresaId, categoriaId, subcategoriaId, "Galaxy Buds",
            null, null, TipoProduto.Fisico, null, null, false,
            null, null, null, null, null, null, null, null, null);

        var result = await useCase.ExecuteAsync(command);

        await produtoRepository.Received(1).InsertAsync(Arg.Is<Produto>(p =>
            p.SubcategoriaId == subcategoriaId));
        unitOfWork.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task Deve_falhar_quando_subcategoria_nao_pertence_a_categoria_informada()
    {
        var produtoRepository = Substitute.For<IProdutoRepository>();
        var categoriaRepository = Substitute.For<ICategoriaRepository>();
        var caracteristicaRepository = Substitute.For<IProdutoCaracteristicaRepository>();
        var embalagemRepository = Substitute.For<IProdutoEmbalagemRepository>();
        var variacaoRepository = Substitute.For<IProdutoVariacaoRepository>();
        var unitOfWork = new FakeUnitOfWork();
        var logger = Substitute.For<ILogger<CadastrarProdutoUseCase>>();

        var useCase = new CadastrarProdutoUseCase(
            produtoRepository, categoriaRepository, caracteristicaRepository,
            embalagemRepository, variacaoRepository, unitOfWork, logger);

        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        var outraCategoriaId = Guid.NewGuid();
        var subcategoriaId = Guid.NewGuid();

        categoriaRepository.GetByIdAsync(empresaId, categoriaId)
            .Returns(new Categoria { Id = categoriaId, EmpresaId = empresaId, Nome = "Eletronicos" });
        // subcategoria pertence a outra categoria
        categoriaRepository.GetByIdAsync(empresaId, subcategoriaId)
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
        var unitOfWork = new FakeUnitOfWork();
        var logger = Substitute.For<ILogger<CadastrarProdutoUseCase>>();

        var useCase = new CadastrarProdutoUseCase(
            produtoRepository, categoriaRepository, caracteristicaRepository,
            embalagemRepository, variacaoRepository, unitOfWork, logger);

        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        var variacaoId = Guid.NewGuid();

        categoriaRepository.GetByIdAsync(empresaId, categoriaId)
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

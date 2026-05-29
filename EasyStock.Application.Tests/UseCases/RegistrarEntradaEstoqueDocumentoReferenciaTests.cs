using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.RegistrarEntradaEstoque;
using Microsoft.Extensions.Logging;
using NSubstitute.ExceptionExtensions;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>
/// Cobre ramificacoes nao exercitadas pelo conjunto principal: propagacao de
/// DocumentoReferencia ate MovimentacaoEstoque, validacoes de quantidade,
/// caminhos defensivos de loja, contexto de auditoria e fallback do gerador
/// de descricao por IA.
/// </summary>
public class RegistrarEntradaEstoqueDocumentoReferenciaTests
{
    private static (RegistrarEntradaEstoqueUseCase useCase,
        IItemEstoqueRepository itemRepo,
        IMovimentacaoEstoqueRepository movRepo,
        IUnitOfWork uow,
        Produto produto,
        Guid empresaId) BuildHappyPath(
            ICurrentUserAccessor? currentUser = null,
            IGeradorDescricaoAnuncio? gerador = null)
    {
        var produtoRepository = Substitute.For<IProdutoRepository>();
        var variacaoRepository = Substitute.For<IProdutoVariacaoRepository>();
        var itemRepository = Substitute.For<IItemEstoqueRepository>();
        var movimentacaoRepository = Substitute.For<IMovimentacaoEstoqueRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var logger = Substitute.For<ILogger<RegistrarEntradaEstoqueUseCase>>();
        var empresaId = Guid.NewGuid();

        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Nome = "Galaxy Buds FE",
            Status = StatusProduto.Ativo
        };

        produtoRepository.GetByIdAsync(produto.Id).Returns(produto);

        var useCase = new RegistrarEntradaEstoqueUseCase(
            produtoRepository,
            variacaoRepository,
            itemRepository,
            movimentacaoRepository,
            unitOfWork,
            logger,
            gerador,
            null,
            null,
            null,
            currentUser);

        return (useCase, itemRepository, movimentacaoRepository, unitOfWork, produto, empresaId);
    }

    private static RegistrarEntradaEstoqueCommand CommandPadrao(
        Guid empresaId,
        Guid produtoId,
        int quantidade = 10,
        string? documentoReferencia = null,
        string? descricaoAnuncio = "Descricao manual") =>
        new(
            empresaId,
            produtoId,
            null,
            quantidade,
            250m,
            null,
            new DateTime(2026, 4, 3, 12, 0, 0, DateTimeKind.Utc),
            NaturezaMovimentacaoEstoque.Compra,
            "CAP3426",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            descricaoAnuncio,
            documentoReferencia,
            null,
            null);

    [Fact]
    public async Task DocumentoReferencia_e_propagado_para_MovimentacaoEstoque_no_formato_pedidoId_itemId()
    {
        var (useCase, _, movRepo, _, produto, empresaId) = BuildHappyPath();
        var docRef = $"{Guid.NewGuid()}:{Guid.NewGuid()}";

        await useCase.ExecuteAsync(CommandPadrao(empresaId, produto.Id, documentoReferencia: docRef));

        await movRepo.Received(1).InsertAsync(Arg.Is<MovimentacaoEstoque>(m =>
            m.DocumentoReferencia == docRef));
    }

    [Fact]
    public async Task DocumentoReferencia_null_chega_como_null_em_MovimentacaoEstoque()
    {
        var (useCase, _, movRepo, _, produto, empresaId) = BuildHappyPath();

        await useCase.ExecuteAsync(CommandPadrao(empresaId, produto.Id, documentoReferencia: null));

        await movRepo.Received(1).InsertAsync(Arg.Is<MovimentacaoEstoque>(m =>
            m.DocumentoReferencia == null));
    }

    [Fact]
    public async Task DocumentoReferencia_vazio_e_normalizado_como_null()
    {
        var (useCase, _, movRepo, _, produto, empresaId) = BuildHappyPath();

        await useCase.ExecuteAsync(CommandPadrao(empresaId, produto.Id, documentoReferencia: "   "));

        await movRepo.Received(1).InsertAsync(Arg.Is<MovimentacaoEstoque>(m =>
            m.DocumentoReferencia == null));
    }

    [Fact]
    public async Task DocumentoReferencia_com_espacos_e_trimado()
    {
        var (useCase, _, movRepo, _, produto, empresaId) = BuildHappyPath();

        await useCase.ExecuteAsync(CommandPadrao(empresaId, produto.Id, documentoReferencia: "  PED-123:ITEM-456  "));

        await movRepo.Received(1).InsertAsync(Arg.Is<MovimentacaoEstoque>(m =>
            m.DocumentoReferencia == "PED-123:ITEM-456"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public async Task Quantidade_zero_ou_negativa_lanca_QuantidadeInvalida(int quantidade)
    {
        var (useCase, itemRepo, movRepo, uow, produto, empresaId) = BuildHappyPath();

        // Quantidade <= 0 e validada antes do GetByIdAsync — usar quantidade explicita.
        // Como o command tem [Range(1, int.MaxValue)] o teste pula validacao de DataAnnotations
        // (nao executada nesse use case), mas o guard interno protege.
        var act = () => useCase.ExecuteAsync(CommandPadrao(empresaId, produto.Id, quantidade: quantidade));
        await act.Should().ThrowAsync<QuantidadeInvalidaException>();

        await itemRepo.DidNotReceive().InsertAsync(Arg.Any<ItemEstoque>());
        await movRepo.DidNotReceive().InsertAsync(Arg.Any<MovimentacaoEstoque>());
        await uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task EmpresaId_vazio_lanca_UseCaseValidationException()
    {
        var (useCase, _, _, _, produto, _) = BuildHappyPath();

        var act = () => useCase.ExecuteAsync(CommandPadrao(Guid.Empty, produto.Id));
        (await act.Should().ThrowAsync<UseCaseValidationException>())
            .WithMessage("*EmpresaId*");
    }

    [Fact]
    public async Task Produto_nao_encontrado_lanca_UseCaseValidationException()
    {
        var produtoRepository = Substitute.For<IProdutoRepository>();
        var variacaoRepository = Substitute.For<IProdutoVariacaoRepository>();
        var itemRepository = Substitute.For<IItemEstoqueRepository>();
        var movimentacaoRepository = Substitute.For<IMovimentacaoEstoqueRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var logger = Substitute.For<ILogger<RegistrarEntradaEstoqueUseCase>>();
        var empresaId = Guid.NewGuid();

        produtoRepository.GetByIdAsync(Arg.Any<Guid>()).Returns((Produto?)null);

        var useCase = new RegistrarEntradaEstoqueUseCase(
            produtoRepository,
            variacaoRepository,
            itemRepository,
            movimentacaoRepository,
            unitOfWork,
            logger);

        var act = () => useCase.ExecuteAsync(CommandPadrao(empresaId, Guid.NewGuid()));
        (await act.Should().ThrowAsync<UseCaseValidationException>())
            .WithMessage("*Produto nao encontrado*");
    }

    [Fact]
    public async Task LojaId_informado_sem_lojaRepository_lanca_UseCaseValidationException()
    {
        var (useCase, _, _, _, produto, empresaId) = BuildHappyPath();

        var command = CommandPadrao(empresaId, produto.Id) with { LojaId = Guid.NewGuid() };

        var act = () => useCase.ExecuteAsync(command);
        (await act.Should().ThrowAsync<UseCaseValidationException>())
            .WithMessage("*loja indisponivel*");
    }

    [Fact]
    public async Task Variacao_nao_encontrada_lanca_UseCaseValidationException()
    {
        var produtoRepository = Substitute.For<IProdutoRepository>();
        var variacaoRepository = Substitute.For<IProdutoVariacaoRepository>();
        var itemRepository = Substitute.For<IItemEstoqueRepository>();
        var movimentacaoRepository = Substitute.For<IMovimentacaoEstoqueRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var logger = Substitute.For<ILogger<RegistrarEntradaEstoqueUseCase>>();
        var empresaId = Guid.NewGuid();

        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Nome = "Galaxy Buds",
            Status = StatusProduto.Ativo
        };
        produtoRepository.GetByIdAsync(produto.Id).Returns(produto);
        variacaoRepository.GetByIdAsync(Arg.Any<Guid>()).Returns((ProdutoVariacao?)null);

        var useCase = new RegistrarEntradaEstoqueUseCase(
            produtoRepository, variacaoRepository, itemRepository,
            movimentacaoRepository, unitOfWork, logger);

        var command = CommandPadrao(empresaId, produto.Id) with { ProdutoVariacaoId = Guid.NewGuid() };

        var act = () => useCase.ExecuteAsync(command);
        (await act.Should().ThrowAsync<UseCaseValidationException>())
            .WithMessage("*Variacao*nao encontrada*");
    }

    [Fact]
    public async Task Variacao_de_outro_produto_lanca_UseCaseValidationException()
    {
        var produtoRepository = Substitute.For<IProdutoRepository>();
        var variacaoRepository = Substitute.For<IProdutoVariacaoRepository>();
        var itemRepository = Substitute.For<IItemEstoqueRepository>();
        var movimentacaoRepository = Substitute.For<IMovimentacaoEstoqueRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var logger = Substitute.For<ILogger<RegistrarEntradaEstoqueUseCase>>();
        var empresaId = Guid.NewGuid();

        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Nome = "Galaxy Buds",
            Status = StatusProduto.Ativo
        };
        var variacaoOutroProduto = new ProdutoVariacao
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = Guid.NewGuid(),
            Nome = "Grafite",
            Ativa = true
        };
        produtoRepository.GetByIdAsync(produto.Id).Returns(produto);
        variacaoRepository.GetByIdAsync(variacaoOutroProduto.Id).Returns(variacaoOutroProduto);

        var useCase = new RegistrarEntradaEstoqueUseCase(
            produtoRepository, variacaoRepository, itemRepository,
            movimentacaoRepository, unitOfWork, logger);

        var command = CommandPadrao(empresaId, produto.Id) with { ProdutoVariacaoId = variacaoOutroProduto.Id };

        var act = () => useCase.ExecuteAsync(command);
        (await act.Should().ThrowAsync<UseCaseValidationException>())
            .WithMessage("*nao pertence ao produto*");
    }

    [Fact]
    public async Task AuditoriaContexto_e_propagado_para_MovimentacaoEstoque_quando_CurrentUser_provido()
    {
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UsuarioId.Returns(Guid.NewGuid());
        currentUser.Ip.Returns("10.0.0.5");
        currentUser.UserAgent.Returns("MAUI Android");
        currentUser.DispositivoId.Returns("dev-001");

        var (useCase, _, movRepo, _, produto, empresaId) = BuildHappyPath(currentUser: currentUser);

        await useCase.ExecuteAsync(CommandPadrao(empresaId, produto.Id));

        await movRepo.Received(1).InsertAsync(Arg.Is<MovimentacaoEstoque>(m =>
            m.UsuarioId == currentUser.UsuarioId &&
            m.Ip == "10.0.0.5" &&
            m.UserAgent == "MAUI Android" &&
            m.DispositivoId == "dev-001"));
    }

    [Fact]
    public async Task UsuarioId_vazio_no_CurrentUser_propaga_como_null_em_MovimentacaoEstoque()
    {
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UsuarioId.Returns(Guid.Empty);
        currentUser.Ip.Returns((string?)null);

        var (useCase, _, movRepo, _, produto, empresaId) = BuildHappyPath(currentUser: currentUser);

        await useCase.ExecuteAsync(CommandPadrao(empresaId, produto.Id));

        await movRepo.Received(1).InsertAsync(Arg.Is<MovimentacaoEstoque>(m => m.UsuarioId == null));
    }

    [Fact]
    public async Task GeradorIA_que_falha_nao_aborta_entrada_e_retorna_sugestao_do_produto()
    {
        var produtoRepository = Substitute.For<IProdutoRepository>();
        var variacaoRepository = Substitute.For<IProdutoVariacaoRepository>();
        var itemRepository = Substitute.For<IItemEstoqueRepository>();
        var movimentacaoRepository = Substitute.For<IMovimentacaoEstoqueRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var gerador = Substitute.For<IGeradorDescricaoAnuncio>();
        var logger = Substitute.For<ILogger<RegistrarEntradaEstoqueUseCase>>();
        var empresaId = Guid.NewGuid();

        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Nome = "Galaxy Buds",
            Status = StatusProduto.Ativo,
            SugestaoDescricaoAnuncio = "Sugestao fallback"
        };
        produtoRepository.GetByIdAsync(produto.Id).Returns(produto);
        gerador.GerarAsync(Arg.Any<Produto>(), Arg.Any<ProdutoVariacao?>(), Arg.Any<ItemEstoque?>(), Arg.Any<string?>())
            .ThrowsAsyncForAnyArgs(new InvalidOperationException("OpenAI down"));

        var useCase = new RegistrarEntradaEstoqueUseCase(
            produtoRepository, variacaoRepository, itemRepository,
            movimentacaoRepository, unitOfWork, logger, gerador);

        var command = CommandPadrao(empresaId, produto.Id, descricaoAnuncio: null);

        var result = await useCase.ExecuteAsync(command);

        await itemRepository.Received(1).InsertAsync(Arg.Is<ItemEstoque>(i =>
            i.DescricaoAnuncio == "Sugestao fallback"));
        await unitOfWork.Received(1).CommitAsync();
    }
}

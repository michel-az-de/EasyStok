using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Events;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.RegistrarSaidaEstoque;
using EasyStock.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>
/// Cobre validacoes nao exercitadas pelos casos felizes/FIFO: itens vazios,
/// quantidade invalida, valor zero em venda comercial, valor absurdo, contexto
/// de auditoria propagado para MovimentacaoEstoque, FIFO puro vs FEFO via
/// ConfiguracaoLoja.
/// </summary>
public class RegistrarSaidaEstoqueValidacoesTests
{
    private static RegistrarSaidaEstoqueUseCase BuildUseCase(
        IProdutoRepository produtoRepository,
        IItemEstoqueRepository itemRepository,
        IVendaRepository vendaRepository,
        IItemVendaRepository itemVendaRepository,
        IMovimentacaoEstoqueRepository movimentacaoRepository,
        IUnitOfWork unitOfWork,
        ICurrentUserAccessor? currentUser = null,
        IConfiguracaoLojaRepository? configuracaoLojaRepository = null)
    {
        unitOfWork.SetupExecuteInTransactionForward<RegistrarSaidaEstoqueResult>();
        return new(
            produtoRepository,
            itemRepository,
            vendaRepository,
            itemVendaRepository,
            movimentacaoRepository,
            unitOfWork,
            Substitute.For<ILogger<RegistrarSaidaEstoqueUseCase>>(),
            Substitute.For<IPublicadorEventos>(), // #306: publicador obrigatorio no caminho de publicacao
            currentUser,
            configuracaoLojaRepository);
    }

    [Fact]
    public async Task EmpresaId_vazio_lanca_UseCaseValidationException()
    {
        var produtoRepository = Substitute.For<IProdutoRepository>();
        var itemRepository = Substitute.For<IItemEstoqueRepository>();
        var vendaRepository = Substitute.For<IVendaRepository>();
        var itemVendaRepository = Substitute.For<IItemVendaRepository>();
        var movimentacaoRepository = Substitute.For<IMovimentacaoEstoqueRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        var useCase = BuildUseCase(produtoRepository, itemRepository, vendaRepository,
            itemVendaRepository, movimentacaoRepository, unitOfWork);

        var act = () => useCase.ExecuteAsync(new RegistrarSaidaEstoqueCommand(
            Guid.Empty,
            [new RegistrarSaidaEstoqueItemCommand(Guid.NewGuid(), 1, 100m, "x")],
            DateTime.UtcNow, DateTime.UtcNow, null, null,
            NaturezaMovimentacaoEstoque.Venda, CanalVenda.LojaPropria, null));

        (await act.Should().ThrowAsync<UseCaseValidationException>())
            .WithMessage("*EmpresaId*");
    }

    [Fact]
    public async Task Sem_itens_lanca_VendaSemItensException()
    {
        var produtoRepository = Substitute.For<IProdutoRepository>();
        var itemRepository = Substitute.For<IItemEstoqueRepository>();
        var vendaRepository = Substitute.For<IVendaRepository>();
        var itemVendaRepository = Substitute.For<IItemVendaRepository>();
        var movimentacaoRepository = Substitute.For<IMovimentacaoEstoqueRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        var useCase = BuildUseCase(produtoRepository, itemRepository, vendaRepository,
            itemVendaRepository, movimentacaoRepository, unitOfWork);

        var act = () => useCase.ExecuteAsync(new RegistrarSaidaEstoqueCommand(
            Guid.NewGuid(),
            Array.Empty<RegistrarSaidaEstoqueItemCommand>(),
            DateTime.UtcNow, DateTime.UtcNow, null, null,
            NaturezaMovimentacaoEstoque.Venda, CanalVenda.LojaPropria, null));

        await act.Should().ThrowAsync<VendaSemItensException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Quantidade_zero_ou_negativa_em_item_lanca_QuantidadeInvalida(int quantidade)
    {
        var produtoRepository = Substitute.For<IProdutoRepository>();
        var itemRepository = Substitute.For<IItemEstoqueRepository>();
        var vendaRepository = Substitute.For<IVendaRepository>();
        var itemVendaRepository = Substitute.For<IItemVendaRepository>();
        var movimentacaoRepository = Substitute.For<IMovimentacaoEstoqueRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var empresaId = Guid.NewGuid();

        var useCase = BuildUseCase(produtoRepository, itemRepository, vendaRepository,
            itemVendaRepository, movimentacaoRepository, unitOfWork);

        var act = () => useCase.ExecuteAsync(new RegistrarSaidaEstoqueCommand(
            empresaId,
            [new RegistrarSaidaEstoqueItemCommand(Guid.NewGuid(), quantidade, 100m, "x")],
            DateTime.UtcNow, DateTime.UtcNow, null, null,
            NaturezaMovimentacaoEstoque.Venda, CanalVenda.LojaPropria, null));

        await act.Should().ThrowAsync<QuantidadeInvalidaException>();
    }

    [Fact]
    public async Task Venda_comercial_com_valor_zero_lanca_UseCaseValidationException()
    {
        var produtoRepository = Substitute.For<IProdutoRepository>();
        var itemRepository = Substitute.For<IItemEstoqueRepository>();
        var vendaRepository = Substitute.For<IVendaRepository>();
        var itemVendaRepository = Substitute.For<IItemVendaRepository>();
        var movimentacaoRepository = Substitute.For<IMovimentacaoEstoqueRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var empresaId = Guid.NewGuid();

        var useCase = BuildUseCase(produtoRepository, itemRepository, vendaRepository,
            itemVendaRepository, movimentacaoRepository, unitOfWork);

        var act = () => useCase.ExecuteAsync(new RegistrarSaidaEstoqueCommand(
            empresaId,
            [new RegistrarSaidaEstoqueItemCommand(Guid.NewGuid(), 1, 0m, "Venda sem preco")],
            DateTime.UtcNow, DateTime.UtcNow, null, null,
            NaturezaMovimentacaoEstoque.Venda, CanalVenda.LojaPropria, null));

        (await act.Should().ThrowAsync<UseCaseValidationException>())
            .WithMessage("*Venda*ValorVendaUnitario*");
    }

    [Fact]
    public async Task Saida_perda_aceita_valor_zero_e_nao_lanca_validacao_de_preco()
    {
        // Sanidade: outras naturezas (Perda, Ajuste, Doacao) podem ter valor 0.
        // O teste passa pelo guard de preco mas falha em outro lugar (item nao
        // encontrado), o que comprova que o guard de preco zero NAO foi acionado.
        var produtoRepository = Substitute.For<IProdutoRepository>();
        var itemRepository = Substitute.For<IItemEstoqueRepository>();
        var vendaRepository = Substitute.For<IVendaRepository>();
        var itemVendaRepository = Substitute.For<IItemVendaRepository>();
        var movimentacaoRepository = Substitute.For<IMovimentacaoEstoqueRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var empresaId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        // Sem stub de GetByIdComLockAsync -> retorna null -> "Item de estoque nao encontrado"
        var useCase = BuildUseCase(produtoRepository, itemRepository, vendaRepository,
            itemVendaRepository, movimentacaoRepository, unitOfWork);

        var act = () => useCase.ExecuteAsync(new RegistrarSaidaEstoqueCommand(
            empresaId,
            [new RegistrarSaidaEstoqueItemCommand(itemId, 1, 0m, "Perda")],
            DateTime.UtcNow, DateTime.UtcNow, null, null,
            NaturezaMovimentacaoEstoque.Perda, CanalVenda.LojaPropria, null));

        (await act.Should().ThrowAsync<UseCaseValidationException>())
            .Where(e => !e.Message.Contains("ValorVendaUnitario"));
    }

    [Fact]
    public async Task ValorVendaUnitario_acima_do_teto_de_sanidade_lanca_UseCaseValidationException()
    {
        var produtoRepository = Substitute.For<IProdutoRepository>();
        var itemRepository = Substitute.For<IItemEstoqueRepository>();
        var vendaRepository = Substitute.For<IVendaRepository>();
        var itemVendaRepository = Substitute.For<IItemVendaRepository>();
        var movimentacaoRepository = Substitute.For<IMovimentacaoEstoqueRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var empresaId = Guid.NewGuid();

        var useCase = BuildUseCase(produtoRepository, itemRepository, vendaRepository,
            itemVendaRepository, movimentacaoRepository, unitOfWork);

        var act = () => useCase.ExecuteAsync(new RegistrarSaidaEstoqueCommand(
            empresaId,
            [new RegistrarSaidaEstoqueItemCommand(Guid.NewGuid(), 1, 200_000_000m, "Erro de digitacao")],
            DateTime.UtcNow, DateTime.UtcNow, null, null,
            NaturezaMovimentacaoEstoque.Venda, CanalVenda.LojaPropria, null));

        (await act.Should().ThrowAsync<UseCaseValidationException>())
            .WithMessage("*teto de sanidade*");
    }

    [Fact]
    public async Task ProdutoId_vazio_em_caminho_FIFO_lanca_UseCaseValidationException()
    {
        // Caminho FIFO/FEFO: nem ItemEstoqueId nem ProdutoId valido.
        // O construtor exige ProdutoId, entao usa-se o construtor do "ItemEstoqueId only"
        // com Empty pra ProdutoId.
        var produtoRepository = Substitute.For<IProdutoRepository>();
        var itemRepository = Substitute.For<IItemEstoqueRepository>();
        var vendaRepository = Substitute.For<IVendaRepository>();
        var itemVendaRepository = Substitute.For<IItemVendaRepository>();
        var movimentacaoRepository = Substitute.For<IMovimentacaoEstoqueRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var empresaId = Guid.NewGuid();

        // Stub: lotes vazios pra produtoId qualquer
        itemRepository.GetLotesDisponiveisParaSaidaAsync(empresaId, Guid.Empty, null, Arg.Any<bool>())
            .Returns(Array.Empty<ItemEstoque>());

        var useCase = BuildUseCase(produtoRepository, itemRepository, vendaRepository,
            itemVendaRepository, movimentacaoRepository, unitOfWork);

        // Usa construtor primario com ItemEstoqueId=null e ProdutoId=Empty (caminho FIFO)
        var item = new RegistrarSaidaEstoqueItemCommand(null, Guid.Empty, null, 1, 100m, "x");

        var act = () => useCase.ExecuteAsync(new RegistrarSaidaEstoqueCommand(
            empresaId, [item], DateTime.UtcNow, DateTime.UtcNow, null, null,
            NaturezaMovimentacaoEstoque.Venda, CanalVenda.LojaPropria, null));

        (await act.Should().ThrowAsync<UseCaseValidationException>())
            .WithMessage("*ProdutoId*");
    }

    [Fact]
    public async Task AuditoriaContexto_e_propagado_para_MovimentacaoEstoque_quando_CurrentUser_provido()
    {
        var produtoRepository = Substitute.For<IProdutoRepository>();
        var itemRepository = Substitute.For<IItemEstoqueRepository>();
        var vendaRepository = Substitute.For<IVendaRepository>();
        var itemVendaRepository = Substitute.For<IItemVendaRepository>();
        var movimentacaoRepository = Substitute.For<IMovimentacaoEstoqueRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var empresaId = Guid.NewGuid();

        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Nome = "Galaxy Buds FE",
            Status = StatusProduto.Ativo
        };

        var item = new ItemEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = produto.Id,
            QuantidadeAtual = Quantidade.From(10),
            QuantidadeInicial = Quantidade.From(10),
            CustoUnitario = Dinheiro.FromDecimal(250m),
            Status = StatusItemEstoque.Ok,
            EntradaEm = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        produtoRepository.GetByIdAsync(produto.Id).Returns(produto);
        itemRepository.GetByIdComLockAsync(empresaId, item.Id).Returns(item);

        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UsuarioId.Returns(Guid.NewGuid());
        currentUser.Ip.Returns("192.168.1.1");
        currentUser.UserAgent.Returns("Test/1.0");
        currentUser.DispositivoId.Returns("dev-99");

        var useCase = BuildUseCase(produtoRepository, itemRepository, vendaRepository,
            itemVendaRepository, movimentacaoRepository, unitOfWork, currentUser);

        await useCase.ExecuteAsync(new RegistrarSaidaEstoqueCommand(
            empresaId,
            [new RegistrarSaidaEstoqueItemCommand(item.Id, 3, 399.90m, "Venda auditada")],
            new DateTime(2026, 4, 3, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 3, 12, 5, 0, DateTimeKind.Utc),
            null, null,
            NaturezaMovimentacaoEstoque.Venda, CanalVenda.LojaPropria, null));

        await movimentacaoRepository.Received(1).InsertRangeAsync(Arg.Is<IEnumerable<MovimentacaoEstoque>>(movs =>
            movs.All(m => m.UsuarioId == currentUser.UsuarioId &&
                          m.Ip == "192.168.1.1" &&
                          m.UserAgent == "Test/1.0" &&
                          m.DispositivoId == "dev-99")));
    }
}

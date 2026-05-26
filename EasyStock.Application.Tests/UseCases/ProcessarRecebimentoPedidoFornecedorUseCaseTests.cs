using EasyStock.Application.Ports.Output.Events;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.Pedido;
using EasyStock.Application.UseCases.RegistrarEntradaEstoque;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Events;
using EasyStock.Domain.Exceptions;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace EasyStock.Application.Tests.UseCases;

public class ProcessarRecebimentoPedidoFornecedorUseCaseTests
{
    private readonly IPedidoFornecedorRepository _pedidoRepository;
    private readonly IPedidoFornecedorItemRepository _itemRepository;
    private readonly RegistrarEntradaEstoqueUseCase _entradaUseCase;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ProcessarRecebimentoPedidoFornecedorUseCase> _logger;
    private readonly IPublicadorEventos _publicador;
    private readonly ProcessarRecebimentoPedidoFornecedorUseCase _useCase;
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly Guid _fornecedorId = Guid.NewGuid();
    private readonly Guid _produtoId = Guid.NewGuid();

    public ProcessarRecebimentoPedidoFornecedorUseCaseTests()
    {
        _pedidoRepository = Substitute.For<IPedidoFornecedorRepository>();
        _itemRepository = Substitute.For<IPedidoFornecedorItemRepository>();
        // RegistrarEntradaEstoqueUseCase é classe concreta com ExecuteAsync virtual: NSubstitute
        // exige todos os args do ctor (6 obrigatórios + 5 opcionais) para criar o proxy.
        _entradaUseCase = Substitute.For<RegistrarEntradaEstoqueUseCase>(
            Substitute.For<IProdutoRepository>(),
            Substitute.For<IProdutoVariacaoRepository>(),
            Substitute.For<IItemEstoqueRepository>(),
            Substitute.For<IMovimentacaoEstoqueRepository>(),
            _unitOfWork,
            Substitute.For<ILogger<RegistrarEntradaEstoqueUseCase>>(),
            null, null, null, null, null);
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _logger = Substitute.For<ILogger<ProcessarRecebimentoPedidoFornecedorUseCase>>();
        _publicador = Substitute.For<IPublicadorEventos>();

        _useCase = new ProcessarRecebimentoPedidoFornecedorUseCase(
            _pedidoRepository,
            _itemRepository,
            _entradaUseCase,
            _unitOfWork,
            _logger,
            _publicador);
    }

    [Fact]
    public async Task ExecuteAsync_Sucesso_ProcessaTodosItensECriaMovimentacoes()
    {
        // Arrange
        var pedidoId = Guid.NewGuid();
        var itemId1 = Guid.NewGuid();
        var itemId2 = Guid.NewGuid();
        var dataRecebimento = new DateTime(2026, 5, 2, 12, 0, 0, DateTimeKind.Utc);

        var pedido = new PedidoFornecedor
        {
            Id = pedidoId,
            EmpresaId = _empresaId,
            FornecedorId = _fornecedorId,
            DataPedido = dataRecebimento.AddDays(-5),
            Status = StatusPedidoFornecedor.Aberto,
            CriadoEm = dataRecebimento.AddDays(-10),
            AlteradoEm = dataRecebimento.AddDays(-10),
            Fornecedor = new Fornecedor { Id = _fornecedorId, Nome = "Fornecedor Teste", EmpresaId = _empresaId }
        };

        var item1 = new PedidoFornecedorItem
        {
            Id = itemId1,
            PedidoFornecedorId = pedidoId,
            ProdutoId = _produtoId,
            Nome = "Produto 1",
            Quantidade = 10,
            QuantidadeRecebida = 0,
            CustoUnitario = 100m,
            CriadoEm = dataRecebimento.AddDays(-10)
        };

        var item2 = new PedidoFornecedorItem
        {
            Id = itemId2,
            PedidoFornecedorId = pedidoId,
            ProdutoId = Guid.NewGuid(),
            Nome = "Produto 2",
            Quantidade = 5,
            QuantidadeRecebida = 0,
            CustoUnitario = 50m,
            CriadoEm = dataRecebimento.AddDays(-10)
        };

        var itensRecebidos = new Dictionary<Guid, decimal>
        {
            { itemId1, 10m },
            { itemId2, 5m }
        };

        var comando = new ProcessarRecebimentoPedidoFornecedorCommand(
            pedidoId,
            _empresaId,
            dataRecebimento,
            itensRecebidos);

        _pedidoRepository.GetByIdAsync(pedidoId).Returns(pedido);
        _itemRepository.GetByPedidoIdAsync(pedidoId, Arg.Any<CancellationToken>())
            .Returns(new[] { item1, item2 });

        var entradaResult = new RegistrarEntradaEstoqueResult(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Descricao",
            "CHAVE-PESQUISA");
        _entradaUseCase.ExecuteAsync(Arg.Any<RegistrarEntradaEstoqueCommand>())
            .Returns(entradaResult);

        // Act
        var resultado = await _useCase.ExecuteAsync(comando);

        // Assert
        resultado.Should().NotBeNull();
        resultado.ItensProcessados.Should().Be(2);
        resultado.Mensagem.Should().Contain("2 itens processados");

        // Verifica chamadas ao entradaUseCase
        await _entradaUseCase.Received(2).ExecuteAsync(Arg.Any<RegistrarEntradaEstoqueCommand>());

        // Verifica atualização dos itens
        await _itemRepository.Received(2).UpdateAsync(Arg.Any<PedidoFornecedorItem>());

        // Verifica atualização do pedido
        await _pedidoRepository.Received(1).UpdateAsync(Arg.Any<PedidoFornecedor>());

        // Verifica commit
        await _unitOfWork.Received(1).CommitAsync();

        // Verifica publicação de eventos
        await _publicador.Received(2).PublicarAsync(Arg.Is<PedidoFornecedorItemRecebido>(e => true));
        await _publicador.Received(1).PublicarAsync(Arg.Is<PedidoFornecedorRecebido>(e => e.TotalItensRecebidos == 2));
    }

    [Fact]
    public async Task ExecuteAsync_SkipItensSemProdutoId()
    {
        // Arrange
        var pedidoId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var dataRecebimento = new DateTime(2026, 5, 2, 12, 0, 0, DateTimeKind.Utc);

        var pedido = new PedidoFornecedor
        {
            Id = pedidoId,
            EmpresaId = _empresaId,
            FornecedorId = _fornecedorId,
            DataPedido = dataRecebimento.AddDays(-5),
            Status = StatusPedidoFornecedor.Aberto,
            CriadoEm = dataRecebimento.AddDays(-10),
            AlteradoEm = dataRecebimento.AddDays(-10),
            Fornecedor = new Fornecedor { Id = _fornecedorId, Nome = "Fornecedor", EmpresaId = _empresaId }
        };

        var itemSemProduto = new PedidoFornecedorItem
        {
            Id = itemId,
            PedidoFornecedorId = pedidoId,
            ProdutoId = null, // SEM PRODUTO
            Nome = "Produto Futuro",
            Quantidade = 5,
            QuantidadeRecebida = 0,
            CustoUnitario = 50m,
            CriadoEm = dataRecebimento.AddDays(-10)
        };

        var comando = new ProcessarRecebimentoPedidoFornecedorCommand(
            pedidoId,
            _empresaId,
            dataRecebimento,
            new Dictionary<Guid, decimal> { { itemId, 5m } });

        _pedidoRepository.GetByIdAsync(pedidoId).Returns(pedido);
        _itemRepository.GetByPedidoIdAsync(pedidoId, Arg.Any<CancellationToken>())
            .Returns(new[] { itemSemProduto });

        // Act
        var resultado = await _useCase.ExecuteAsync(comando);

        // Assert
        resultado.ItensProcessados.Should().Be(0);

        // Não deve chamar entradaUseCase
        await _entradaUseCase.DidNotReceive().ExecuteAsync(Arg.Any<RegistrarEntradaEstoqueCommand>());

        // Mas deve atualizar o item com QuantidadeRecebida
        await _itemRepository.Received(1).UpdateAsync(Arg.Is<PedidoFornecedorItem>(i => i.QuantidadeRecebida == 5m));
    }

    [Fact]
    public async Task ExecuteAsync_SkipQuantidadeZero()
    {
        // Arrange
        var pedidoId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var dataRecebimento = new DateTime(2026, 5, 2, 12, 0, 0, DateTimeKind.Utc);

        var pedido = new PedidoFornecedor
        {
            Id = pedidoId,
            EmpresaId = _empresaId,
            FornecedorId = _fornecedorId,
            DataPedido = dataRecebimento.AddDays(-5),
            Status = StatusPedidoFornecedor.Aberto,
            CriadoEm = dataRecebimento.AddDays(-10),
            AlteradoEm = dataRecebimento.AddDays(-10),
            Fornecedor = new Fornecedor { Id = _fornecedorId, Nome = "Fornecedor", EmpresaId = _empresaId }
        };

        var item = new PedidoFornecedorItem
        {
            Id = itemId,
            PedidoFornecedorId = pedidoId,
            ProdutoId = _produtoId,
            Nome = "Produto",
            Quantidade = 10,
            QuantidadeRecebida = 0,
            CustoUnitario = 100m,
            CriadoEm = dataRecebimento.AddDays(-10)
        };

        var comando = new ProcessarRecebimentoPedidoFornecedorCommand(
            pedidoId,
            _empresaId,
            dataRecebimento,
            new Dictionary<Guid, decimal> { { itemId, 0m } }); // Quantidade 0

        _pedidoRepository.GetByIdAsync(pedidoId).Returns(pedido);
        _itemRepository.GetByPedidoIdAsync(pedidoId, Arg.Any<CancellationToken>())
            .Returns(new[] { item });

        // Act
        var resultado = await _useCase.ExecuteAsync(comando);

        // Assert
        resultado.ItensProcessados.Should().Be(0);
        await _entradaUseCase.DidNotReceive().ExecuteAsync(Arg.Any<RegistrarEntradaEstoqueCommand>());
    }

    [Fact]
    public async Task ExecuteAsync_Idempotente_PedidoJaRecebido()
    {
        // Arrange
        var pedidoId = Guid.NewGuid();
        var dataRecebimento = new DateTime(2026, 5, 2, 12, 0, 0, DateTimeKind.Utc);

        var pedido = new PedidoFornecedor
        {
            Id = pedidoId,
            EmpresaId = _empresaId,
            FornecedorId = _fornecedorId,
            Status = StatusPedidoFornecedor.Recebido, // JÁ RECEBIDO
            CriadoEm = dataRecebimento.AddDays(-10),
            AlteradoEm = dataRecebimento
        };

        var comando = new ProcessarRecebimentoPedidoFornecedorCommand(
            pedidoId,
            _empresaId,
            dataRecebimento,
            new Dictionary<Guid, decimal>());

        _pedidoRepository.GetByIdAsync(pedidoId).Returns(pedido);

        // Act
        var resultado = await _useCase.ExecuteAsync(comando);

        // Assert
        resultado.Mensagem.Should().Contain("já recebido");
        resultado.ItensProcessados.Should().Be(0);

        // Não deve prosseguir
        await _itemRepository.DidNotReceive().GetByPedidoIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_Falha_EmpresaNaoMatch()
    {
        // Arrange
        var pedidoId = Guid.NewGuid();
        var empresaDiferente = Guid.NewGuid();

        var pedido = new PedidoFornecedor
        {
            Id = pedidoId,
            EmpresaId = _empresaId,
            FornecedorId = _fornecedorId
        };

        var comando = new ProcessarRecebimentoPedidoFornecedorCommand(
            pedidoId,
            empresaDiferente, // EMPRESA DIFERENTE
            DateTime.UtcNow,
            new Dictionary<Guid, decimal>());

        _pedidoRepository.GetByIdAsync(pedidoId).Returns(pedido);

        // Act & Assert
        await Assert.ThrowsAsync<UseCaseValidationException>(
            async () => await _useCase.ExecuteAsync(comando));
    }

    [Fact]
    public async Task ExecuteAsync_Falha_PedidoCancelado()
    {
        // Arrange
        var pedidoId = Guid.NewGuid();

        var pedido = new PedidoFornecedor
        {
            Id = pedidoId,
            EmpresaId = _empresaId,
            FornecedorId = _fornecedorId,
            Status = StatusPedidoFornecedor.Cancelado // CANCELADO
        };

        var comando = new ProcessarRecebimentoPedidoFornecedorCommand(
            pedidoId,
            _empresaId,
            DateTime.UtcNow,
            new Dictionary<Guid, decimal>());

        _pedidoRepository.GetByIdAsync(pedidoId).Returns(pedido);

        // Act & Assert
        await Assert.ThrowsAsync<RegraDeDominioVioladaException>(
            async () => await _useCase.ExecuteAsync(comando));
    }

    [Fact]
    public async Task ExecuteAsync_Falha_EntradaEstoqueFalha()
    {
        // Arrange
        var pedidoId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var dataRecebimento = new DateTime(2026, 5, 2, 12, 0, 0, DateTimeKind.Utc);

        var pedido = new PedidoFornecedor
        {
            Id = pedidoId,
            EmpresaId = _empresaId,
            FornecedorId = _fornecedorId,
            DataPedido = dataRecebimento.AddDays(-5),
            Status = StatusPedidoFornecedor.Aberto,
            CriadoEm = dataRecebimento.AddDays(-10),
            AlteradoEm = dataRecebimento.AddDays(-10),
            Fornecedor = new Fornecedor { Id = _fornecedorId, Nome = "Fornecedor", EmpresaId = _empresaId }
        };

        var item = new PedidoFornecedorItem
        {
            Id = itemId,
            PedidoFornecedorId = pedidoId,
            ProdutoId = _produtoId,
            Nome = "Produto",
            Quantidade = 10,
            QuantidadeRecebida = 0,
            CustoUnitario = 100m,
            CriadoEm = dataRecebimento.AddDays(-10)
        };

        var comando = new ProcessarRecebimentoPedidoFornecedorCommand(
            pedidoId,
            _empresaId,
            dataRecebimento,
            new Dictionary<Guid, decimal> { { itemId, 10m } });

        _pedidoRepository.GetByIdAsync(pedidoId).Returns(pedido);
        _itemRepository.GetByPedidoIdAsync(pedidoId, Arg.Any<CancellationToken>())
            .Returns(new[] { item });

        // Simula falha na entrada de estoque
        _entradaUseCase.ExecuteAsync(Arg.Any<RegistrarEntradaEstoqueCommand>())
            .Throws(new Exception("Erro ao criar entrada"));

        // Act & Assert
        await Assert.ThrowsAsync<Exception>(
            async () => await _useCase.ExecuteAsync(comando));
    }

    [Fact]
    public async Task ExecuteAsync_PublicaEventoPedidoRecebido()
    {
        // Arrange
        var pedidoId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var dataRecebimento = new DateTime(2026, 5, 2, 12, 0, 0, DateTimeKind.Utc);

        var pedido = new PedidoFornecedor
        {
            Id = pedidoId,
            EmpresaId = _empresaId,
            FornecedorId = _fornecedorId,
            DataPedido = dataRecebimento.AddDays(-5),
            Status = StatusPedidoFornecedor.Aberto,
            CriadoEm = dataRecebimento.AddDays(-10),
            AlteradoEm = dataRecebimento.AddDays(-10),
            Fornecedor = new Fornecedor { Id = _fornecedorId, Nome = "Fornecedor", EmpresaId = _empresaId }
        };

        var item = new PedidoFornecedorItem
        {
            Id = itemId,
            PedidoFornecedorId = pedidoId,
            ProdutoId = _produtoId,
            Nome = "Produto",
            Quantidade = 10,
            QuantidadeRecebida = 0,
            CustoUnitario = 100m,
            CriadoEm = dataRecebimento.AddDays(-10)
        };

        var comando = new ProcessarRecebimentoPedidoFornecedorCommand(
            pedidoId,
            _empresaId,
            dataRecebimento,
            new Dictionary<Guid, decimal> { { itemId, 10m } });

        _pedidoRepository.GetByIdAsync(pedidoId).Returns(pedido);
        _itemRepository.GetByPedidoIdAsync(pedidoId, Arg.Any<CancellationToken>())
            .Returns(new[] { item });

        var entradaResult = new RegistrarEntradaEstoqueResult(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Descricao",
            "CHAVE-PESQUISA");
        _entradaUseCase.ExecuteAsync(Arg.Any<RegistrarEntradaEstoqueCommand>())
            .Returns(entradaResult);

        // Act
        await _useCase.ExecuteAsync(comando);

        // Assert
        await _publicador.Received(1).PublicarAsync(
            Arg.Is<PedidoFornecedorRecebido>(e =>
                e.PedidoId == pedidoId &&
                e.EmpresaId == _empresaId &&
                e.FornecedorId == _fornecedorId &&
                e.TotalItensRecebidos == 1 &&
                e.DataRecebimento == dataRecebimento));
    }
}

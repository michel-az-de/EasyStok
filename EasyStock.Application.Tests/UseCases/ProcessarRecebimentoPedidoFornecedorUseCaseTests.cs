using EasyStock.Application.Ports.Output.Events;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Tests.Helpers;
using EasyStock.Application.UseCases.Pedido;
using EasyStock.Application.UseCases.RegistrarEntradaEstoque;
using EasyStock.Domain.Events;
using Microsoft.Extensions.Logging;
using NSubstitute.ExceptionExtensions;

namespace EasyStock.Application.Tests.UseCases;

public class ProcessarRecebimentoPedidoFornecedorUseCaseTests
{
    private readonly IPedidoFornecedorRepository _pedidoRepository;
    private readonly RegistrarEntradaEstoqueUseCase _entradaUseCase;
    private readonly IMovimentacaoEstoqueRepository _movimentacaoRepository;
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
        // RegistrarEntradaEstoqueUseCase é classe concreta com ExecuteAsync virtual: NSubstitute
        // exige todos os args do ctor (6 obrigatórios + 6 opcionais) para criar o proxy.
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _entradaUseCase = Substitute.For<RegistrarEntradaEstoqueUseCase>(
            Substitute.For<IProdutoRepository>(),
            Substitute.For<IProdutoVariacaoRepository>(),
            Substitute.For<IItemEstoqueRepository>(),
            Substitute.For<IMovimentacaoEstoqueRepository>(),
            _unitOfWork,
            Substitute.For<ILogger<RegistrarEntradaEstoqueUseCase>>(),
            null, null, null, null, null, null);
        _movimentacaoRepository = Substitute.For<IMovimentacaoEstoqueRepository>();
        _logger = Substitute.For<ILogger<ProcessarRecebimentoPedidoFornecedorUseCase>>();
        _publicador = Substitute.For<IPublicadorEventos>();

        // #1019: o use case agora envolve o corpo em ExecuteInTransactionSemRetryAsync.
        // Sem este setup, o mock retorna default(T) e o corpo nunca executa.
        _unitOfWork.SetupExecuteInTransactionSemRetry<ProcessarRecebimentoPedidoFornecedorResult>();

        _useCase = new ProcessarRecebimentoPedidoFornecedorUseCase(
            _pedidoRepository,
            _entradaUseCase,
            _movimentacaoRepository,
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

        var pedido = new PedidoFornecedor
        {
            Id = pedidoId,
            EmpresaId = _empresaId,
            FornecedorId = _fornecedorId,
            DataPedido = dataRecebimento.AddDays(-5),
            Status = StatusPedidoFornecedor.Aberto,
            CriadoEm = dataRecebimento.AddDays(-10),
            AlteradoEm = dataRecebimento.AddDays(-10),
            Fornecedor = new Fornecedor { Id = _fornecedorId, Nome = "Fornecedor Teste", EmpresaId = _empresaId },
            Itens = { item1, item2 }
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

        _pedidoRepository.GetByIdWithItensAsync(pedidoId, Arg.Any<CancellationToken>())
            .Returns(pedido);

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

        // Verifica atualização do pedido
        await _pedidoRepository.Received(1).UpdateAsync(Arg.Any<PedidoFornecedor>());

        // Verifica publicação de eventos
        await _publicador.Received(2).PublicarAsync(Arg.Is<PedidoFornecedorItemRecebido>(e => true));
        await _publicador.Received(1).PublicarAsync(Arg.Is<PedidoFornecedorRecebido>(e => e.TotalItensRecebidos == 2));
    }

    [Fact]
    public async Task ExecuteAsync_Idempotente_NaoRecriaEntrada_QuandoReferenciaJaExiste()
    {
        // #790: retry apos falha parcial — a movimentacao de referencia ja existe.
        var pedidoId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var data = new DateTime(2026, 5, 2, 12, 0, 0, DateTimeKind.Utc);

        var item = new PedidoFornecedorItem
        {
            Id = itemId, PedidoFornecedorId = pedidoId, ProdutoId = _produtoId,
            Nome = "P", Quantidade = 10, QuantidadeRecebida = 0, CustoUnitario = 100m,
            CriadoEm = data.AddDays(-10)
        };

        var pedido = new PedidoFornecedor
        {
            Id = pedidoId, EmpresaId = _empresaId, FornecedorId = _fornecedorId,
            DataPedido = data.AddDays(-5), Status = StatusPedidoFornecedor.Aberto,
            CriadoEm = data.AddDays(-10), AlteradoEm = data.AddDays(-10),
            Fornecedor = new Fornecedor { Id = _fornecedorId, Nome = "F", EmpresaId = _empresaId },
            Itens = { item }
        };

        _pedidoRepository.GetByIdWithItensAsync(pedidoId, Arg.Any<CancellationToken>()).Returns(pedido);

        var refDoc = $"{pedidoId}:{itemId}:r10";
        _movimentacaoRepository
            .ExisteReferenciaAsync(_empresaId, _produtoId, refDoc, NaturezaMovimentacaoEstoque.Compra, Arg.Any<CancellationToken>())
            .Returns(true);

        var comando = new ProcessarRecebimentoPedidoFornecedorCommand(
            pedidoId, _empresaId, data, new Dictionary<Guid, decimal> { { itemId, 10m } });

        await _useCase.ExecuteAsync(comando);

        // Nao recria a entrada (senao dobra estoque), mas persiste o total recebido.
        await _entradaUseCase.DidNotReceive().ExecuteAsync(Arg.Any<RegistrarEntradaEstoqueCommand>());
        await _pedidoRepository.Received(1).UpdateAsync(Arg.Is<PedidoFornecedor>(p =>
            p.Itens.Single().QuantidadeRecebida == 10m));
    }

    [Fact]
    public async Task ExecuteAsync_SkipItensSemProdutoId()
    {
        // Arrange
        var pedidoId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var dataRecebimento = new DateTime(2026, 5, 2, 12, 0, 0, DateTimeKind.Utc);

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

        var pedido = new PedidoFornecedor
        {
            Id = pedidoId,
            EmpresaId = _empresaId,
            FornecedorId = _fornecedorId,
            DataPedido = dataRecebimento.AddDays(-5),
            Status = StatusPedidoFornecedor.Aberto,
            CriadoEm = dataRecebimento.AddDays(-10),
            AlteradoEm = dataRecebimento.AddDays(-10),
            Fornecedor = new Fornecedor { Id = _fornecedorId, Nome = "Fornecedor", EmpresaId = _empresaId },
            Itens = { itemSemProduto }
        };

        var comando = new ProcessarRecebimentoPedidoFornecedorCommand(
            pedidoId,
            _empresaId,
            dataRecebimento,
            new Dictionary<Guid, decimal> { { itemId, 5m } });

        _pedidoRepository.GetByIdWithItensAsync(pedidoId, Arg.Any<CancellationToken>()).Returns(pedido);

        // Act
        var resultado = await _useCase.ExecuteAsync(comando);

        // Assert
        resultado.ItensProcessados.Should().Be(0);

        // Não deve chamar entradaUseCase
        await _entradaUseCase.DidNotReceive().ExecuteAsync(Arg.Any<RegistrarEntradaEstoqueCommand>());

        // O item deve ter QuantidadeRecebida atualizada
        itemSemProduto.QuantidadeRecebida.Should().Be(5m);
    }

    [Fact]
    public async Task ExecuteAsync_SkipQuantidadeZero()
    {
        // Arrange
        var pedidoId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var dataRecebimento = new DateTime(2026, 5, 2, 12, 0, 0, DateTimeKind.Utc);

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

        var pedido = new PedidoFornecedor
        {
            Id = pedidoId,
            EmpresaId = _empresaId,
            FornecedorId = _fornecedorId,
            DataPedido = dataRecebimento.AddDays(-5),
            Status = StatusPedidoFornecedor.Aberto,
            CriadoEm = dataRecebimento.AddDays(-10),
            AlteradoEm = dataRecebimento.AddDays(-10),
            Fornecedor = new Fornecedor { Id = _fornecedorId, Nome = "Fornecedor", EmpresaId = _empresaId },
            Itens = { item }
        };

        var comando = new ProcessarRecebimentoPedidoFornecedorCommand(
            pedidoId,
            _empresaId,
            dataRecebimento,
            new Dictionary<Guid, decimal> { { itemId, 0m } }); // Quantidade 0

        _pedidoRepository.GetByIdWithItensAsync(pedidoId, Arg.Any<CancellationToken>()).Returns(pedido);

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

        _pedidoRepository.GetByIdWithItensAsync(pedidoId, Arg.Any<CancellationToken>()).Returns(pedido);

        // Act
        var resultado = await _useCase.ExecuteAsync(comando);

        // Assert
        resultado.Mensagem.Should().Contain("já recebido");
        resultado.ItensProcessados.Should().Be(0);

        // Não deve prosseguir
        await _pedidoRepository.DidNotReceive().UpdateAsync(Arg.Any<PedidoFornecedor>());
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

        _pedidoRepository.GetByIdWithItensAsync(pedidoId, Arg.Any<CancellationToken>()).Returns(pedido);

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

        _pedidoRepository.GetByIdWithItensAsync(pedidoId, Arg.Any<CancellationToken>()).Returns(pedido);

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

        var pedido = new PedidoFornecedor
        {
            Id = pedidoId,
            EmpresaId = _empresaId,
            FornecedorId = _fornecedorId,
            DataPedido = dataRecebimento.AddDays(-5),
            Status = StatusPedidoFornecedor.Aberto,
            CriadoEm = dataRecebimento.AddDays(-10),
            AlteradoEm = dataRecebimento.AddDays(-10),
            Fornecedor = new Fornecedor { Id = _fornecedorId, Nome = "Fornecedor", EmpresaId = _empresaId },
            Itens = { item }
        };

        var comando = new ProcessarRecebimentoPedidoFornecedorCommand(
            pedidoId,
            _empresaId,
            dataRecebimento,
            new Dictionary<Guid, decimal> { { itemId, 10m } });

        _pedidoRepository.GetByIdWithItensAsync(pedidoId, Arg.Any<CancellationToken>()).Returns(pedido);

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

        var pedido = new PedidoFornecedor
        {
            Id = pedidoId,
            EmpresaId = _empresaId,
            FornecedorId = _fornecedorId,
            DataPedido = dataRecebimento.AddDays(-5),
            Status = StatusPedidoFornecedor.Aberto,
            CriadoEm = dataRecebimento.AddDays(-10),
            AlteradoEm = dataRecebimento.AddDays(-10),
            Fornecedor = new Fornecedor { Id = _fornecedorId, Nome = "Fornecedor", EmpresaId = _empresaId },
            Itens = { item }
        };

        var comando = new ProcessarRecebimentoPedidoFornecedorCommand(
            pedidoId,
            _empresaId,
            dataRecebimento,
            new Dictionary<Guid, decimal> { { itemId, 10m } });

        _pedidoRepository.GetByIdWithItensAsync(pedidoId, Arg.Any<CancellationToken>()).Returns(pedido);

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

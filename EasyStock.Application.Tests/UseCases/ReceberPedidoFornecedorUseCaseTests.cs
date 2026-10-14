using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Financeiro.Integracao;
using EasyStock.Application.UseCases.Pedido;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases;

public class ReceberPedidoFornecedorUseCaseTests
{
    private readonly IPedidoFornecedorRepository _pedidoRepo = Substitute.For<IPedidoFornecedorRepository>();
    private readonly IPedidoFornecedorItemRepository _itemRepo = Substitute.For<IPedidoFornecedorItemRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ReceberPedidoCompletoUseCase _receberCompleto;
    private readonly Guid _empresaId = Guid.NewGuid();

    public ReceberPedidoFornecedorUseCaseTests()
    {
        _receberCompleto = Substitute.For<ReceberPedidoCompletoUseCase>(
            Substitute.For<IPedidoFornecedorItemRepository>(), null!);
    }

    private ReceberPedidoFornecedorUseCase Build() => new(
        _pedidoRepo, _itemRepo, _receberCompleto,
        // Sem LojaId no pedido a conta a pagar automática não se aplica; os repositórios ficam mudos.
        new GerarContaPagarDePedidoFornecedorUseCase(
            Substitute.For<IContaPagarRepository>(), Substitute.For<ICategoriaFinanceiraRepository>(),
            Substitute.For<IConfiguracaoLojaRepository>(), null!,
            Substitute.For<ILogger<GerarContaPagarDePedidoFornecedorUseCase>>()),
        _uow, Substitute.For<ILogger<ReceberPedidoFornecedorUseCase>>());

    private PedidoFornecedor Pedido() => new()
    {
        Id = Guid.NewGuid(), EmpresaId = _empresaId, FornecedorId = Guid.NewGuid(),
        DataPedido = DateTime.UtcNow, Status = StatusPedidoFornecedor.Aberto,
        CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow
    };

    [Fact]
    public async Task Pedido_com_itens_da_entrada_no_estoque_pelo_recebimento_completo()
    {
        var pedido = Pedido();
        _pedidoRepo.GetByIdAsync(pedido.Id).Returns(pedido);
        _itemRepo.GetByPedidoIdAsync(pedido.Id, Arg.Any<CancellationToken>()).Returns(new[]
        {
            new PedidoFornecedorItem { Id = Guid.NewGuid(), PedidoFornecedorId = pedido.Id, Nome = "Farinha", Quantidade = 3 }
        });

        await Build().ExecuteAsync(new ReceberPedidoFornecedorCommand(pedido.Id, _empresaId, null, "BR123"));

        await _receberCompleto.Received(1).ExecuteAsync(
            Arg.Is<ReceberPedidoCompletoCommand>(c => c.PedidoId == pedido.Id && c.EmpresaId == _empresaId),
            Arg.Any<CancellationToken>());
        pedido.Tracking.Should().Be("BR123");
    }

    [Fact]
    public async Task Pedido_sem_itens_so_marca_recebido()
    {
        var pedido = Pedido();
        _pedidoRepo.GetByIdAsync(pedido.Id).Returns(pedido);
        _itemRepo.GetByPedidoIdAsync(pedido.Id, Arg.Any<CancellationToken>()).Returns(Array.Empty<PedidoFornecedorItem>());

        await Build().ExecuteAsync(new ReceberPedidoFornecedorCommand(pedido.Id, _empresaId, null, null));

        pedido.Status.Should().Be(StatusPedidoFornecedor.Recebido);
        await _receberCompleto.DidNotReceive().ExecuteAsync(Arg.Any<ReceberPedidoCompletoCommand>(), Arg.Any<CancellationToken>());
        await _uow.Received(1).CommitAsync();
    }
}

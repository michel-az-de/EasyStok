using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services;
using EasyStock.Application.UseCases.RemoverItemPedido;
using EasyStock.Application.UseCases.RemoverPagamentoPedido;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>
/// #768: a remoção de item/pagamento passou a ser rastreada (pedido.Itens.Remove /
/// pedido.Pagamentos.Remove) em vez de ExecuteDeleteAsync imediato fora do UoW.
/// O DELETE participa do mesmo SaveChanges do recálculo do total + evento (atômico).
/// </summary>
public class RemoverItemPagamentoPedidoUseCaseTests
{
    private readonly IPedidoRepository _repo = Substitute.For<IPedidoRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private RemoverItemPedidoUseCase ItemUC() =>
        new(_repo,
            new PedidoEstoqueIntegrationService(
                Substitute.For<IItemEstoqueRepository>(),
                Substitute.For<IMovimentacaoEstoqueRepository>(),
                Microsoft.Extensions.Options.Options.Create(new PedidoEstoqueOptions()),
                Substitute.For<ILogger<PedidoEstoqueIntegrationService>>()),
            _uow, Substitute.For<ILogger<RemoverItemPedidoUseCase>>());
    private RemoverPagamentoPedidoUseCase PagUC() =>
        new(_repo, _uow, Substitute.For<ILogger<RemoverPagamentoPedidoUseCase>>());

    private static PedidoItem NovoItem(Guid pedidoId, string nome, decimal qtd, decimal preco) => new()
    {
        Id = Guid.NewGuid(), PedidoId = pedidoId, Nome = nome,
        Quantidade = qtd, PrecoUnitario = preco, Subtotal = qtd * preco, CriadoEm = DateTime.UtcNow
    };

    [Fact]
    public async Task RemoverItem_TiraDaColecao_RecalculaTotal_E_GravaEvento_SemDeleteImediato()
    {
        var empresaId = Guid.NewGuid();
        var pedido = Pedido.Criar(empresaId);
        var itemA = NovoItem(pedido.Id, "A", 2, 10m);   // 20
        var itemB = NovoItem(pedido.Id, "B", 1, 50m);   // 50
        pedido.Itens.Add(itemA);
        pedido.Itens.Add(itemB);
        pedido.RecalcularTotal();
        _repo.GetByIdWithDetailsAsync(empresaId, pedido.Id).Returns(pedido);

        PedidoEvento? evento = null;
        await _repo.AddEventoAsync(Arg.Do<PedidoEvento>(e => evento = e));

        await ItemUC().ExecuteAsync(new RemoverItemPedidoCommand(empresaId, pedido.Id, itemA.Id));

        pedido.Itens.Should().ContainSingle(i => i.Id == itemB.Id);
        pedido.Itens.Should().NotContain(i => i.Id == itemA.Id);   // remoção rastreada => Deleted no SaveChanges
        pedido.Total.Valor.Should().Be(50m);                        // recalculado na mesma unidade
        evento!.Tipo.Should().Be("item_removed");
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task RemoverPagamento_TiraDaColecao_E_GravaEvento_NaMesmaUnidade()
    {
        var empresaId = Guid.NewGuid();
        var pedido = Pedido.Criar(empresaId);
        var pag = new PedidoPagamento
        {
            Id = Guid.NewGuid(), PedidoId = pedido.Id, Metodo = "pix", Valor = 30m, PagoEm = DateTime.UtcNow
        };
        pedido.Pagamentos.Add(pag);
        _repo.GetByIdWithDetailsAsync(empresaId, pedido.Id).Returns(pedido);

        PedidoEvento? evento = null;
        await _repo.AddEventoAsync(Arg.Do<PedidoEvento>(e => evento = e));

        await PagUC().ExecuteAsync(new RemoverPagamentoPedidoCommand(empresaId, pedido.Id, pag.Id));

        pedido.Pagamentos.Should().BeEmpty();
        evento!.Tipo.Should().Be("pagamento_removido");
        await _uow.Received(1).CommitAsync();
    }
}

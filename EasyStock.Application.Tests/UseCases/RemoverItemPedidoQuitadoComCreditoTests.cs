using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Application.Services;
using EasyStock.Application.Services.Pedidos;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Application.UseCases.RemoverItemPedido;
using EasyStock.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>
/// #2733: #1724-5 trava a remoção de item quando o pedido já tem valor recebido: a comanda confere o saldo e devolve
/// (cupom ou estorno), o DELETE direto não. Pedido quitado com crédito do cliente não tem PedidoPagamento
/// (TotalPago = 0), então a trava não via o crédito e o item saía sem devolver o valor ao cliente.
/// </summary>
public class RemoverItemPedidoQuitadoComCreditoTests
{
    private readonly IPedidoRepository _repo = Substitute.For<IPedidoRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IEstornoOnlineRepository _estornos = Substitute.For<IEstornoOnlineRepository>();
    private readonly ICreditoClienteComandaRepository _creditos = Substitute.For<ICreditoClienteComandaRepository>();
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly Pedido _pedido;
    private readonly PedidoItem _item;

    public RemoverItemPedidoQuitadoComCreditoTests()
    {
        _pedido = Pedido.Criar(_empresaId);
        _item = new PedidoItem { Id = Guid.NewGuid(), PedidoId = _pedido.Id, Nome = "Bolo", Quantidade = 1,
            PrecoUnitario = 50m, Subtotal = 50m, CriadoEm = DateTime.UtcNow };
        _pedido.Itens.Add(_item);
        _pedido.Itens.Add(new PedidoItem { Id = Guid.NewGuid(), PedidoId = _pedido.Id, Nome = "Torta", Quantidade = 1,
            PrecoUnitario = 20m, Subtotal = 20m, CriadoEm = DateTime.UtcNow });
        _pedido.RecalcularTotal();
        _repo.GetByIdWithDetailsAsync(_empresaId, _pedido.Id).Returns(_pedido);
        _repo.ListarEstornosManuaisAsync(default, default, default).ReturnsForAnyArgs([]);
        _estornos.ListarAsync(default, default, default).ReturnsForAnyArgs([]);
        _estornos.RecebimentosAsync(default, default, default).ReturnsForAnyArgs([]);
    }

    private RemoverItemPedidoUseCase UseCase() =>
        new(_repo,
            new PedidoEstoqueIntegrationService(
                Substitute.For<IItemEstoqueRepository>(),
                Substitute.For<IMovimentacaoEstoqueRepository>(),
                Substitute.For<EasyStock.Application.Ports.Output.Integration.IPublicadorEventoIntegracao>(),
                Microsoft.Extensions.Options.Options.Create(new PedidoEstoqueOptions()),
                Substitute.For<ILogger<PedidoEstoqueIntegrationService>>()),
            _uow, Substitute.For<ILogger<RemoverItemPedidoUseCase>>(),
            new SaldoFinanceiroPedido(_repo, _estornos, _creditos));

    [Fact]
    public async Task Pedido_quitado_com_credito_do_cliente_recusa_remover_item_pelo_delete_direto()
    {
        _pedido.TotalPago.Should().Be(0m);
        _creditos.TotaisDoPedidoAsync(default, default, default).ReturnsForAnyArgs((0m, 70m));

        var act = () => UseCase().ExecuteAsync(new RemoverItemPedidoCommand(_empresaId, _pedido.Id, _item.Id));

        (await act.Should().ThrowAsync<CobrancaPedidoConflitoException>())
            .Which.Codigo.Should().Be(CobrancaPedidoConflitoException.ValorRecebido);
        _pedido.Itens.Should().Contain(_item);
        _pedido.Total.Should().Be(Dinheiro.FromDecimal(70m));
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task Pedido_sem_pagamento_nem_credito_segue_removendo_item()
    {
        _creditos.TotaisDoPedidoAsync(default, default, default).ReturnsForAnyArgs((0m, 0m));

        await UseCase().ExecuteAsync(new RemoverItemPedidoCommand(_empresaId, _pedido.Id, _item.Id));

        _pedido.Itens.Should().NotContain(_item);
        _pedido.Total.Should().Be(Dinheiro.FromDecimal(20m));
        await _uow.Received(1).CommitAsync();
    }
}

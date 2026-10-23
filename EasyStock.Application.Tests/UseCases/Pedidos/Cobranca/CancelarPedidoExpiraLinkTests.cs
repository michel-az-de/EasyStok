using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services;
using EasyStock.Application.Services.Pedidos;
using EasyStock.Application.UseCases.CancelarPedido;
using EasyStock.Domain.Enums.Pagamentos;
using EasyStock.Domain.Sales;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EasyStock.Application.Tests.UseCases.Pedidos.Cobranca;

/// <summary>
/// PEDIDO-05 (banca 23/10): cancelar o pedido cancelava a cobranca no sistema, mas o link do Mercado Pago seguia
/// aceitando pagamento; quem pagasse depois caia no "paga e estorna". O cancelamento expira a preferencia dos links
/// online que cancelou, depois do commit (mesmo padrao da quitacao manual e da troca de forma).
/// </summary>
public class CancelarPedidoExpiraLinkTests
{
    [Fact]
    public async Task Cancelar_expira_a_preferencia_do_link_online_pendente()
    {
        var f = new CobrancaPedidoFixture(StatusPedidoMapper.AguardandoPagamento);
        var online = f.AdicionarOnline(referencia: "pref-aberta");

        await Cancelar(f).ExecuteAsync(new CancelarPedidoCommand(f.EmpresaId, f.Pedido.Id, Motivo: "Cliente desistiu"));

        online.Status.Should().Be(StatusCobrancaPedido.Cancelada);
        await f.MpClient.Received(1).ExpirarPreferenciaAsync("pref-aberta", Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancelar_nao_expira_nada_quando_so_ha_cobranca_na_entrega()
    {
        var f = new CobrancaPedidoFixture(StatusPedidoMapper.AguardandoPagamento);
        f.AdicionarNaEntrega();

        await Cancelar(f).ExecuteAsync(new CancelarPedidoCommand(f.EmpresaId, f.Pedido.Id));

        await f.MpClient.DidNotReceiveWithAnyArgs().ExpirarPreferenciaAsync(default!, default, default);
    }

    [Fact]
    public async Task Cancelar_pedido_ja_cancelado_nao_expira_de_novo()
    {
        var f = new CobrancaPedidoFixture(StatusPedidoMapper.Cancelado);
        f.AdicionarOnline(referencia: "pref-aberta");

        await Cancelar(f).ExecuteAsync(new CancelarPedidoCommand(f.EmpresaId, f.Pedido.Id));

        await f.MpClient.DidNotReceiveWithAnyArgs().ExpirarPreferenciaAsync(default!, default, default);
    }

    [Fact]
    public async Task Cancelar_com_falha_ao_expirar_no_provedor_ainda_cancela()
    {
        var f = new CobrancaPedidoFixture(StatusPedidoMapper.AguardandoPagamento);
        f.AdicionarOnline(referencia: "pref-aberta");
        f.MpClient.ExpirarPreferenciaAsync(Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new HttpRequestException("mp fora"));

        var resultado = await Cancelar(f).ExecuteAsync(new CancelarPedidoCommand(f.EmpresaId, f.Pedido.Id));

        resultado!.Status.Should().Be(StatusPedidoMapper.Cancelado, "o commit ja houve; a expiracao e best-effort");
    }

    private static CancelarPedidoUseCase Cancelar(CobrancaPedidoFixture f)
    {
        var estoque = new PedidoEstoqueIntegrationService(Substitute.For<IItemEstoqueRepository>(),
            Substitute.For<IMovimentacaoEstoqueRepository>(), f.Publicador, Options.Create(new PedidoEstoqueOptions()),
            NullLogger<PedidoEstoqueIntegrationService>.Instance);
        return new CancelarPedidoUseCase(f.PedidoRepo, estoque,
            new EfeitosCancelamentoPedido(Substitute.For<IContaReceberRepository>(), f.VagaRepo, NullLogger<EfeitosCancelamentoPedido>.Instance),
            f.Uow, NullLogger<CancelarPedidoUseCase>.Instance, f.CobrancaRepo, f.Publicador, f.OperacaoEventos,
            expiracao: f.Expiracao());
    }
}

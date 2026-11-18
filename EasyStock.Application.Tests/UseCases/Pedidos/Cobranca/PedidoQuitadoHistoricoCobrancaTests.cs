using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Enums.Pagamentos;

namespace EasyStock.Application.Tests.UseCases.Pedidos.Cobranca;

/// <summary>
/// #2813: o pedido entregue do histórico já foi pago na origem e entra quitado pela marca <c>QuitadoHistoricoEm</c>.
/// Gerar cobrança ou trocar a forma de pagamento criaria link ou cobrança sobre dinheiro já recebido, então os dois
/// recusam o pedido marcado pela própria marca, sem depender de o status continuar final.
/// </summary>
public class PedidoQuitadoHistoricoCobrancaTests
{
    /// <param name="status">
    /// <c>entregue</c> é o pedido do histórico como o import o grava; <c>aguardando</c> é o marcado que perdeu o status
    /// final (a guarda de pedido finalizado não o pegaria): só a marca o recusa.
    /// </param>
    private static CobrancaPedidoFixture QuitadoNoHistorico(string status)
    {
        var f = new CobrancaPedidoFixture("entregue");
        f.Pedido.MarcarQuitadoHistorico(CobrancaPedidoFixture.Agora);
        f.Pedido.Status = status;
        return f;
    }

    [Theory]
    [InlineData("entregue")]
    [InlineData("aguardando")]
    public async Task Gerar_cobranca_no_pedido_quitado_no_historico_e_recusada_e_nao_cria_nada(string status)
    {
        var f = QuitadoNoHistorico(status);

        var cobrar = () => f.Gerar().ExecuteAsync(new GerarCobrancaPedidoInput(f.EmpresaId, f.Pedido.Id));

        (await cobrar.Should().ThrowAsync<UseCaseValidationException>()).Which.Message.Should().Contain("quitado");
        f.Cobrancas.Should().BeEmpty();
        f.Preferencias.Should().BeEmpty("nenhum link é pedido ao provedor");
    }

    [Fact]
    public async Task Gerar_cobranca_no_pedido_comum_segue_como_hoje()
    {
        var f = new CobrancaPedidoFixture();

        var r = await f.Gerar().ExecuteAsync(new GerarCobrancaPedidoInput(f.EmpresaId, f.Pedido.Id));

        r.LinkPagamento.Should().Be("https://mp.test/pref-1");
        f.Cobrancas.Should().ContainSingle(c => c.Status == StatusCobrancaPedido.Pendente);
    }

    [Theory]
    [InlineData("entregue", "online")]
    [InlineData("entregue", "na_entrega")]
    [InlineData("aguardando", "online")]
    [InlineData("aguardando", "na_entrega")]
    public async Task Trocar_forma_no_pedido_quitado_no_historico_e_recusada_e_nao_cria_cobranca(string status, string forma)
    {
        var f = QuitadoNoHistorico(status);

        var trocar = () => f.Trocar().ExecuteAsync(new TrocarFormaPagamentoPedidoInput(f.EmpresaId, f.Pedido.Id, forma));

        (await trocar.Should().ThrowAsync<UseCaseValidationException>()).Which.Message.Should().Contain("quitado");
        f.Cobrancas.Should().BeEmpty();
        f.Preferencias.Should().BeEmpty("nenhum link é pedido ao provedor");
        f.Pedido.Status.Should().Be(status, "a troca recusada não mexe no pedido");
    }

    [Fact]
    public async Task Trocar_forma_no_pedido_comum_segue_como_hoje()
    {
        var f = new CobrancaPedidoFixture();

        var r = await f.Trocar().ExecuteAsync(new TrocarFormaPagamentoPedidoInput(f.EmpresaId, f.Pedido.Id, "na_entrega"));

        r.Cobranca.Provedor.Should().Be(CobrancaPedido.ProvedorNaEntrega);
        f.Cobrancas.Should().ContainSingle(c => c.Status == StatusCobrancaPedido.Pendente);
    }
}

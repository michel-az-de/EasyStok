using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Enums.Pagamentos;
using EasyStock.Domain.Exceptions.Storefront;
using NSubstitute.ExceptionExtensions;

namespace EasyStock.Application.Tests.UseCases.Pedidos.Cobranca;

/// <summary>
/// S11: a cobrança do pedido é uma preferência do Mercado Pago com <c>external_reference</c> do pedido,
/// itens e frete, que expira em 30 minutos. Pedir de novo, com a pendente ainda válida, devolve a mesma.
/// </summary>
public class GerarCobrancaPedidoUseCaseTests
{
    [Fact]
    public async Task PreferenciaComExternalReferenceEExpiracao()
    {
        var f = new CobrancaPedidoFixture();
        var conversaId = Guid.NewGuid();

        var r = await f.Gerar().ExecuteAsync(new GerarCobrancaPedidoInput(f.EmpresaId, f.Pedido.Id, conversaId));

        var pref = f.Preferencias.Should().ContainSingle().Subject;
        pref.PedidoId.Should().Be(f.Pedido.Id, "external_reference = PedidoId");
        pref.Items.Select(i => i.Titulo).Should().Equal("Brigadeiro", "Frete SP Centro");
        pref.ValorTotal.Should().Be(25m);
        pref.ExpiraEm.Should().Be(CobrancaPedidoFixture.Agora.AddMinutes(30));
        pref.IdempotencyKey.Should().NotBeNullOrWhiteSpace();

        var cobranca = f.Cobrancas.Should().ContainSingle().Subject;
        cobranca.Status.Should().Be(StatusCobrancaPedido.Pendente);
        cobranca.Provedor.Should().Be(CobrancaPedido.ProvedorMercadoPago);
        cobranca.ReferenciaExterna.Should().Be("pref-1");
        cobranca.LinkPagamento.Should().Be("https://mp.test/pref-1");
        cobranca.Valor.Should().Be(25m);
        cobranca.ExpiraEm.Should().Be(CobrancaPedidoFixture.Agora.AddMinutes(30));
        cobranca.Tentativa.Should().Be(1);
        cobranca.ConversaId.Should().Be(conversaId);

        r.LinkPagamento.Should().Be("https://mp.test/pref-1");
        r.Reutilizada.Should().BeFalse();
        await f.Uow.Received().CommitAsync();
    }

    [Fact]
    public async Task IdempotentePorTentativa()
    {
        var f = new CobrancaPedidoFixture();
        var uc = f.Gerar();
        var input = new GerarCobrancaPedidoInput(f.EmpresaId, f.Pedido.Id);

        var primeira = await uc.ExecuteAsync(input);
        var segunda = await uc.ExecuteAsync(input);

        segunda.CobrancaId.Should().Be(primeira.CobrancaId);
        segunda.LinkPagamento.Should().Be(primeira.LinkPagamento);
        segunda.Reutilizada.Should().BeTrue();
        f.Preferencias.Should().ContainSingle("a pendente ainda válida é devolvida sem nova preferência");
        f.Cobrancas.Should().ContainSingle();
    }

    [Fact]
    public async Task PendenteVencida_ExpiraEGeraOutraComChaveNova()
    {
        var f = new CobrancaPedidoFixture();
        var vencida = f.AdicionarOnline();
        f.Relogio.Agora = CobrancaPedidoFixture.Agora.AddMinutes(31);

        await f.Gerar().ExecuteAsync(new GerarCobrancaPedidoInput(f.EmpresaId, f.Pedido.Id));

        vencida.Status.Should().Be(StatusCobrancaPedido.Expirada);
        f.Cobrancas.Should().HaveCount(2);
        f.Preferencias.Single().IdempotencyKey.Should().EndWith("-2", "a chave muda a cada cobrança do pedido");
    }

    [Fact]
    public async Task PedidoJaPago_Recusa()
    {
        var f = new CobrancaPedidoFixture();
        f.AdicionarOnline().MarcarPaga("pay-1", 25m, "pix", CobrancaPedidoFixture.Agora);

        var act = () => f.Gerar().ExecuteAsync(new GerarCobrancaPedidoInput(f.EmpresaId, f.Pedido.Id));

        (await act.Should().ThrowAsync<CobrancaPedidoConflitoException>()).Which.Codigo.Should().Be("pedido_ja_pago");
    }

    [Fact]
    public async Task MercadoPagoFora_LancaIndisponivelSemGravar()
    {
        var f = new CobrancaPedidoFixture();
        f.MpClient.CriarPreferenceAsync(Arg.Any<CriarPreferenceCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("500"));

        var act = () => f.Gerar().ExecuteAsync(new GerarCobrancaPedidoInput(f.EmpresaId, f.Pedido.Id));

        await act.Should().ThrowAsync<MercadoPagoIndisponivelException>();
        f.Cobrancas.Should().BeEmpty();
    }
}

using EasyStock.Application.Services.Storefront;
using EasyStock.Domain.Entities.Storefront;

namespace EasyStock.Application.Tests.Services.Storefront;

/// <summary>
/// #2018 (comanda-15): caracterização do checkout para o que a listagem de janelas omite (dia bloqueado, janela bloqueada).
/// Listagem e checkout passam a decidir "a janela atende a data" pela mesma regra (<see cref="JanelaEntrega.AvaliarEm"/>);
/// as mensagens ao cliente continuam as mesmas.
/// </summary>
public class CheckoutJanelaBloqueadaTests
{
    private static void Bloquear(CheckoutCoreServiceTests.Cenario c, Guid? janelaEspecifica)
    {
        c.BloqueioRepo.GetByStorefrontPeriodoAsync(c.Storefront.Id, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new List<BloqueioEntrega> { BloqueioEntrega.Criar(c.Storefront.Id, c.DataEntrega, "Feriado", janelaEspecifica) });
    }

    private static CheckoutCoreInput Input(CheckoutCoreServiceTests.Cenario c) => new(
        ClienteId: Guid.NewGuid(),
        Itens: [new(c.CardapioItemId, 1, null)],
        JanelaId: c.JanelaId, DataEntrega: c.DataEntrega, Cep: "01310100", Origem: "atendimento", Slug: "casa-da-baba");

    [Fact]
    public async Task DiaInteiroBloqueado_RecusaComAMensagemDeDataSemEntrega()
    {
        var c = new CheckoutCoreServiceTests.Cenario();
        Bloquear(c, janelaEspecifica: null);

        var agir = () => c.Servico().CriarPedidoComReservaAsync(Input(c));

        (await agir.Should().ThrowAsync<RegraDeDominioVioladaException>()).WithMessage("Não há entrega no dia 02/06/2026*");
        c.PedidosAdicionados.Should().BeEmpty();
    }

    [Fact]
    public async Task JanelaEspecificaBloqueada_RecusaComAMensagemDeDataSemEntrega()
    {
        var c = new CheckoutCoreServiceTests.Cenario();
        Bloquear(c, janelaEspecifica: c.JanelaId);

        var agir = () => c.Servico().CriarPedidoComReservaAsync(Input(c));

        (await agir.Should().ThrowAsync<RegraDeDominioVioladaException>()).WithMessage("Não há entrega no dia 02/06/2026*");
    }

    [Fact]
    public async Task BloqueioDeOutraJanela_NaoImpedeEstaJanela()
    {
        var c = new CheckoutCoreServiceTests.Cenario();
        Bloquear(c, janelaEspecifica: Guid.NewGuid());

        var reservado = await c.Servico().CriarPedidoComReservaAsync(Input(c));

        reservado.Pedido.Should().NotBeNull();
    }

    [Fact]
    public async Task JanelaDesativada_RecusaComAMensagemDeNaoDisponivel()
    {
        var c = new CheckoutCoreServiceTests.Cenario();
        (await c.JanelaRepo.GetByIdAsync(c.JanelaId))!.Desativar();

        var agir = () => c.Servico().CriarPedidoComReservaAsync(Input(c));

        (await agir.Should().ThrowAsync<RegraDeDominioVioladaException>()).WithMessage("*não está mais disponível*");
    }
}

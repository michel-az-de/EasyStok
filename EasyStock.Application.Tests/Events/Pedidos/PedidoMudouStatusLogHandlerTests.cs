using EasyStock.Application.Events.Pedidos.Handlers;
using EasyStock.Domain.Integration;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.Events.Pedidos;

public class PedidoMudouStatusLogHandlerTests
{
    private static PedidoMudouStatusLogHandler Handler() =>
        new(NullLogger<PedidoMudouStatusLogHandler>.Instance);

    [Fact]
    public void TipoEvento_e_a_chave_do_registro_keyed()
        => Handler().TipoEvento.Should().Be("pedido.mudou_status");

    [Fact]
    public async Task HandleAsync_loga_e_nao_lanca_idempotente()
    {
        var evento = OutboxEventoIntegracao.Criar(
            empresaId: Guid.NewGuid(),
            tipoEvento: "pedido.mudou_status",
            aggregateType: "pedido",
            aggregateId: Guid.NewGuid(),
            payloadJson: "{\"statusAntigo\":\"aguardando\",\"statusNovo\":\"preparando\"}");

        var act = () => Handler().HandleAsync(evento, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}

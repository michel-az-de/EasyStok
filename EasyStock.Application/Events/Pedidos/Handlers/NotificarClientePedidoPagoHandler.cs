using System.Text.Json;
using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Automacoes;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Domain.Integration;

namespace EasyStock.Application.Events.Pedidos.Handlers;

/// <summary>
/// Pagamento online confirmado (S11, <see cref="PedidoPagoEvent"/>) vira
/// <see cref="TipoEventoNotificacao.PedidoPagoConfirmado"/> ao cliente pelo WhatsApp (S13): "recebemos seu
/// pagamento, pedido nº …, previsão …". Idempotente por cobrança e pagamento externo.
/// <para>
/// #1991-2: a automática "Pagamento confirmado" nasce ligada e roda no mesmo evento. Quando ela vai sair na conversa
/// do pedido, este aviso cede a vez: o cliente recebe um "recebemos seu pagamento", não dois. Fora da janela (ou com a
/// automática desligada pela dona) segue este aviso, que tem modelo aprovado.
/// </para>
/// </summary>
public sealed class NotificarClientePedidoPagoHandler(
    AvisoStatusPedidoCliente aviso, ITenantContextAccessor? tenant = null, DispararAutomacaoUseCase? automatica = null)
    : IIntegrationEventHandler
{
    public const string MarcoPago = "pago";

    private static readonly JsonSerializerOptions Camel = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public string TipoEvento => PedidoPagoEvent.TipoEvento;

    public async Task HandleAsync(OutboxEventoIntegracao evento, CancellationToken ct)
    {
        var pago = JsonSerializer.Deserialize<PedidoPagoEvent>(evento.PayloadJson, Camel);
        if (pago is null) return;

        if (automatica is not null && tenant is not null)
        {
            // O dispatcher roda sem claim JWT: sem o tenant, o filtro global e a RLS zeram a leitura da regra e da conversa.
            tenant.SetCurrentTenant(evento.EmpresaId);
            if (await automatica.VaiSairAsync(new DisparoAutomacao(
                    evento.EmpresaId, GatilhoAutomacao.PagamentoConfirmado, pago.ConversaId, pago.ClienteId, pago.PedidoId), ct))
                return;
        }

        var marco = pago.CobrancaPedidoId == Guid.Empty ? MarcoPago : $"pagamento:{pago.CobrancaPedidoId:N}:{pago.PagamentoExternoId}";
        await aviso.EnfileirarAsync(TipoEventoNotificacao.PedidoPagoConfirmado, evento.EmpresaId, pago.PedidoId, marco, ct);
    }
}

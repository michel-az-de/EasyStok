using System.Text.Json;
using EasyStock.Application.Events.Pedidos;
using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.UseCases.Atendimento.Automacoes;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Integration;
using EasyStock.Domain.Sales;

namespace EasyStock.Application.Events.Atendimento;

/// <summary>Enfileirado no outbox quando a primeira mensagem de um contato abre uma conversa (S42).</summary>
public sealed record ConversaAbertaEvent(Guid ConversaId, Guid? ClienteId)
{
    public const string TipoEvento = "conversa.aberta";
}

/// <summary>Enfileirado no outbox quando a dona encerra a conversa pelo console (S42).</summary>
public sealed record ConversaEncerradaEvent(Guid ConversaId, Guid? ClienteId)
{
    public const string TipoEvento = "conversa.encerrada";
}

/// <summary>
/// Base dos handlers das automáticas (S42, ADR-0030). O dispatcher do outbox roda sem usuário: o
/// handler fixa o tenant do evento antes de ler qualquer tabela com RLS. O disparo nunca lança por
/// regra de negócio, então o evento não volta para retry por uma regra desligada.
/// </summary>
public abstract class AutomacaoHandlerBase(ITenantContextAccessor tenant) : IIntegrationEventHandler
{
    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public abstract string TipoEvento { get; }

    public Task HandleAsync(OutboxEventoIntegracao evento, CancellationToken ct)
    {
        tenant.SetCurrentTenant(evento.EmpresaId);
        return TratarAsync(evento, ct);
    }

    protected abstract Task TratarAsync(OutboxEventoIntegracao evento, CancellationToken ct);

    protected static T? Ler<T>(OutboxEventoIntegracao evento) => JsonSerializer.Deserialize<T>(evento.PayloadJson, Json);
}

/// <summary><c>conversa.aberta</c>: primeiro contato, fora do horário ou loja fechada, só uma delas (S40).</summary>
public sealed class AutomacaoConversaAbertaHandler(ITenantContextAccessor tenant, DispararAutomacaoUseCase disparo)
    : AutomacaoHandlerBase(tenant)
{
    public override string TipoEvento => ConversaAbertaEvent.TipoEvento;

    protected override async Task TratarAsync(OutboxEventoIntegracao evento, CancellationToken ct)
    {
        if (Ler<ConversaAbertaEvent>(evento) is { } e)
            await disparo.DispararPrimeiraEntradaAsync(evento.EmpresaId, e.ConversaId, ct);
    }
}

/// <summary><c>conversa.encerrada</c>: mensagem de encerramento.</summary>
public sealed class AutomacaoConversaEncerradaHandler(ITenantContextAccessor tenant, DispararAutomacaoUseCase disparo)
    : AutomacaoHandlerBase(tenant)
{
    public override string TipoEvento => ConversaEncerradaEvent.TipoEvento;

    protected override async Task TratarAsync(OutboxEventoIntegracao evento, CancellationToken ct)
    {
        if (Ler<ConversaEncerradaEvent>(evento) is { } e)
            await disparo.ExecuteAsync(new DisparoAutomacao(evento.EmpresaId, GatilhoAutomacao.Encerramento, e.ConversaId, e.ClienteId, null), ct);
    }
}

/// <summary><c>pedido.pago</c> (S11): pagamento confirmado.</summary>
public sealed class AutomacaoPedidoPagoHandler(ITenantContextAccessor tenant, DispararAutomacaoUseCase disparo)
    : AutomacaoHandlerBase(tenant)
{
    public override string TipoEvento => PedidoPagoEvent.TipoEvento;

    /// <summary>Só o que a automática usa do <see cref="PedidoPagoEvent"/>.</summary>
    private sealed record PedidoPagoPayload(Guid PedidoId, Guid? ClienteId, Guid? ConversaId);

    protected override async Task TratarAsync(OutboxEventoIntegracao evento, CancellationToken ct)
    {
        if (Ler<PedidoPagoPayload>(evento) is { } e)
            await disparo.ExecuteAsync(new DisparoAutomacao(
                evento.EmpresaId, GatilhoAutomacao.PagamentoConfirmado, e.ConversaId, e.ClienteId, e.PedidoId), ct);
    }
}

/// <summary>
/// <c>pedido.mudou_status</c> para <c>entregue</c>: pós-entrega. Os demais status são avisos de esteira
/// do S13 e não passam por aqui.
/// </summary>
public sealed class AutomacaoPedidoEntregueHandler(
    ITenantContextAccessor tenant, DispararAutomacaoUseCase disparo, IPedidoRepository pedidos)
    : AutomacaoHandlerBase(tenant)
{
    public const string Tipo = "pedido.mudou_status";

    private sealed record PedidoStatusPayload(Guid PedidoId, string? StatusNovo);

    public override string TipoEvento => Tipo;

    protected override async Task TratarAsync(OutboxEventoIntegracao evento, CancellationToken ct)
    {
        if (Ler<PedidoStatusPayload>(evento) is not { } e || e.StatusNovo != StatusPedidoMapper.Entregue) return;

        var pedido = await pedidos.GetByIdAsync(evento.EmpresaId, e.PedidoId);
        if (pedido?.ClienteId is not { } clienteId) return;

        await disparo.ExecuteAsync(new DisparoAutomacao(evento.EmpresaId, GatilhoAutomacao.PosEntrega, null, clienteId, pedido.Id), ct);
    }
}

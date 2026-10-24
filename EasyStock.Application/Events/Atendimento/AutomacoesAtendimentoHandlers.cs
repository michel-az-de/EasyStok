using System.Text.Json;
using EasyStock.Application.Events.Pedidos;
using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Automacoes;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Enums.Notifications;
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

/// <summary>
/// <c>pedido.pago</c> (S11): o "recebemos seu pagamento" ao cliente, decidido num lugar só (#1991-2). Primeiro a
/// automática "Pagamento confirmado" (nasce ligada; a dona troca o texto ou desliga); se ela não sai (desligada, fora
/// da janela, sem conversa, variável sem valor, falha do canal que se sabe que nada saiu), o aviso S13 de pedido pago, com
/// modelo aprovado e idempotente por cobrança e pagamento externo. Assim o cliente recebe um aviso e quase nunca dois. O envio
/// incerto (o canal não respondeu) não cai no S13, para não duplicar; a equipe é avisada no sino e a falha fica na conversa.
/// </summary>
public sealed class AutomacaoPedidoPagoHandler(
    ITenantContextAccessor tenant, DispararAutomacaoUseCase disparo, AvisoStatusPedidoCliente aviso,
    AvisoEquipeNoAparelho? equipe = null)
    : AutomacaoHandlerBase(tenant)
{
    /// <summary>Marco do aviso S13 quando o pagamento não tem cobrança (crédito do cliente).</summary>
    public const string MarcoPago = "pago";

    /// <summary>#2007: o obrigado do pagamento que quita o pedido em entrega ou entregue.</summary>
    public const string TextoPagoNaEntrega = "Recebemos o pagamento. Obrigada!";

    public override string TipoEvento => PedidoPagoEvent.TipoEvento;

    /// <summary>Só o que o aviso usa do <see cref="PedidoPagoEvent"/>.</summary>
    private sealed record PedidoPagoPayload(
        Guid PedidoId, Guid? ClienteId, Guid? ConversaId, Guid CobrancaPedidoId, string? PagamentoExternoId, string? StatusPedido,
        string? Provedor);

    protected override async Task TratarAsync(OutboxEventoIntegracao evento, CancellationToken ct)
    {
        if (Ler<PedidoPagoPayload>(evento) is not { } e) return;
        // #2007 (item 7): pagamento que quita um pedido que já saiu ou foi entregue (na porta, ou o link pago com o
        // pedido na rua). "Pedido confirmado para o dia X" seria texto velho: sai só o obrigado, uma vez.
        if (e.StatusPedido is StatusPedidoMapper.SaiuParaEntrega or StatusPedidoMapper.Entregue)
        {
            await aviso.EnfileirarAsync(TipoEventoNotificacao.ComandaAtualizada, evento.EmpresaId, e.PedidoId,
                MarcoDoPagamento(e), ct, mensagem: TextoPagoNaEntrega);
            return;
        }

        var manual = e.Provedor != CobrancaPedido.ProvedorMercadoPago;
        var automatica = await disparo.ExecuteAsync(new DisparoAutomacao(
            evento.EmpresaId, GatilhoAutomacao.PagamentoConfirmado, e.ConversaId, e.ClienteId, e.PedidoId,
            PagamentoManual: manual), ct);
        if (automatica is ResultadoAutomacao.Enviada or ResultadoAutomacao.PedidoJaAdiante) return;
        if (automatica == ResultadoAutomacao.EnvioIncerto)
        {
            // O canal não respondeu: a automática pode ter chegado, e o S13 seria o segundo "recebemos seu pagamento".
            // Mas também pode não ter chegado: a falha fica na conversa e a equipe é avisada no sino para conferir.
            if (equipe is not null) await equipe.PagamentoSemAvisoAoClienteAsync(evento.EmpresaId, e.PedidoId, ct);
            return;
        }

        var marco = e.CobrancaPedidoId == Guid.Empty ? MarcoPago : $"pagamento:{e.CobrancaPedidoId:N}:{e.PagamentoExternoId}";
        await aviso.EnfileirarAsync(TipoEventoNotificacao.PedidoPagoConfirmado, evento.EmpresaId, e.PedidoId, marco, ct,
            pagamentoManual: manual);
    }

    private static string MarcoDoPagamento(PedidoPagoPayload e) =>
        e.CobrancaPedidoId == Guid.Empty ? MarcoPago : $"pagamento:{e.CobrancaPedidoId:N}:{e.PagamentoExternoId}";
}

/// <summary>
/// <c>pedido.mudou_status</c> para <c>entregue</c>: pós-entrega. Os demais status são avisos de esteira
/// do S13 e não passam por aqui.
/// </summary>
public sealed class AutomacaoPedidoEntregueHandler(
    ITenantContextAccessor tenant, DispararAutomacaoUseCase disparo, IPedidoRepository pedidos,
    IConversaRepository? conversas = null)
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

        // #2004: o agradecimento vai pela conversa dona do pedido; a mais recente do cliente pode ser de outro canal.
        // Sem conversa do pedido, o disparo cai na mais recente.
        var conversaId = conversas is null ? null : await conversas.ObterIdPorPedidoAsync(evento.EmpresaId, pedido.Id, ct);
        await disparo.ExecuteAsync(new DisparoAutomacao(evento.EmpresaId, GatilhoAutomacao.PosEntrega, conversaId, clienteId, pedido.Id), ct);
    }
}

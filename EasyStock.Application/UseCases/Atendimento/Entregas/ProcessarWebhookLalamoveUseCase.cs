using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Integration.Logistica;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.Entregas;

public enum DesfechoWebhookLalamove
{
    /// <summary>Sondagem do cadastro da URL, evento de carteira ou pedido que não é nosso.</summary>
    Ignorado,
    AssinaturaInvalida,
    Duplicado,
    Processado,
}

/// <summary>
/// Webhook da Lalamove (#2160, ADR-0064 D5). O corpo só diz "olhe este pedido": acha a corrida pelo id do pedido
/// (a loja vem dela), confere a chave e a assinatura com a conexão da loja, deduplica em <c>WebhookRecebido</c> e
/// reconsulta o provedor. O corpo nunca é gravado, só o hash. Evento de pedido desconhecido responde 200: a
/// Lalamove desativa a URL depois de 10 falhas seguidas.
/// </summary>
public sealed class ProcessarWebhookLalamoveUseCase(
    ICorridaExternaRepository corridas, IWebhookRecebidoRepository webhooks, ITenantContextAccessor tenant,
    ILogisticaGateway gateway, SincronizarCorridaUseCase sincronizar, ILogger<ProcessarWebhookLalamoveUseCase> logger)
{
    public const string Provedor = "Lalamove";

    public async Task<DesfechoWebhookLalamove> ExecuteAsync(string corpo, CancellationToken ct = default)
    {
        var evento = Ler(corpo);
        if (evento?.OrderId is null) return DesfechoWebhookLalamove.Ignorado;

        var localizada = await corridas.LocalizarPorIdExternoAsync(EmpresaEntregador.Lalamove, evento.OrderId, ct);
        if (localizada is null && evento.PrevOrderId is { } anterior)
            localizada = await corridas.LocalizarPorIdExternoAsync(EmpresaEntregador.Lalamove, anterior, ct);
        if (localizada is null)
        {
            logger.LogInformation("Webhook Lalamove {Tipo} de pedido sem corrida no EasyStok; ignorado.", evento.Tipo);
            return DesfechoWebhookLalamove.Ignorado;
        }

        tenant.SetCurrentTenant(localizada.EmpresaId);
        if (!await gateway.WebhookAutenticoAsync(localizada.EmpresaId, corpo, ct))
        {
            // A reconsulta de contingência cobre: nada muda por um corpo que não passou na assinatura.
            logger.LogWarning("Webhook Lalamove com chave ou assinatura que não confere. empresaId={EmpresaId}", localizada.EmpresaId);
            return DesfechoWebhookLalamove.AssinaturaInvalida;
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(corpo)));
        var eventId = evento.EventId ?? hash;
        var registro = await webhooks.TryRegistrarAsync(Provedor, eventId, hash, ct);
        if (registro is null)
        {
            var existente = await webhooks.ObterAsync(Provedor, eventId, ct);
            if (existente is null || existente.Sucesso) return DesfechoWebhookLalamove.Duplicado;
            registro = existente; // #787: tentativa anterior sem sucesso é reprocessada.
        }

        try
        {
            if (evento.Tipo == "ORDER_REPLACED" && evento.PrevOrderId is { } prev)
                await sincronizar.SubstituirPedidoAsync(localizada.EmpresaId, localizada.CorridaId, prev, evento.OrderId, ct);
            else
                await sincronizar.ExecuteAsync(localizada.EmpresaId, localizada.CorridaId, ct);
            await webhooks.MarcarProcessadoAsync(registro.Id, sucesso: true, ct: ct);
            return DesfechoWebhookLalamove.Processado;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await webhooks.MarcarProcessadoAsync(registro.Id, sucesso: false, erro: ex.GetType().Name + ": " + ex.Message, ct: ct);
            throw;
        }
    }

    private sealed record Evento(string? EventId, string? Tipo, string? OrderId, string? PrevOrderId);

    private static Evento? Ler(string corpo)
    {
        try
        {
            using var doc = JsonDocument.Parse(corpo);
            var raiz = doc.RootElement;
            if (raiz.ValueKind != JsonValueKind.Object) return null;
            var data = raiz.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object ? d : default;
            var order = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("order", out var o) && o.ValueKind == JsonValueKind.Object ? o : default;
            return new Evento(
                Texto(raiz, "eventId"),
                Texto(raiz, "eventType"),
                Texto(order, "orderId") ?? Texto(data, "orderId"),
                Texto(data, "prevOrderId") ?? Texto(order, "previousOrderId") ?? Texto(order, "prevOrderId"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Texto(JsonElement e, string nome) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(nome, out var v)
            ? v.ValueKind switch { JsonValueKind.String => v.GetString(), JsonValueKind.Number => v.GetRawText(), _ => null }
            : null;
}

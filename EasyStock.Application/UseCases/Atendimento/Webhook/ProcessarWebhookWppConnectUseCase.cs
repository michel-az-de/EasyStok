using System.Text.Json;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Integration.Crypto;
using EasyStock.Application.Ports.Output.Observability;
using EasyStock.Domain.Integration;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.UseCases.Atendimento.Webhook;

public sealed class ProcessarWebhookWppConnectUseCase(
    IIntegrationCredentialResolver credenciais, ITenantContextAccessor tenant,
    IWppConnectSessaoClient cliente, ProcessarEventoWhatsAppUseCase processar, IMenusNumeradosWppConnect? menus = null,
    ILogger<ProcessarWebhookWppConnectUseCase>? logger = null, IOperationalMetrics? metricas = null)
{
    public async Task<bool?> ExecuteAsync(string rawBody, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(rawBody);
        var root = doc.RootElement;
        var nome = WppConnectWebhookParser.Texto(root, "session") ?? "";
        if (CredencialWppConnect.EmpresaDaSessao(nome) is not { } empresaId) return null;
        tenant.SetCurrentTenant(empresaId);
        var sessao = await credenciais.ObterAsync<CredencialWppConnect>(empresaId,
            CredencialWppConnect.ProviderKey, AmbienteIntegracao.Production, ct);
        if (sessao?.Sessao != nome) return null;

        string? telefone = null;
        var evento = WppConnectWebhookParser.Texto(root, "event");
        var contato = WppConnectWebhookParser.Texto(root, evento == "onselfmessage" ? "to" : "from");
        if (contato?.EndsWith("@lid", StringComparison.Ordinal) == true)
            telefone = await cliente.ResolverTelefoneAsync(sessao, contato, ct);
        rawBody = await IdAusenteWppConnect.CompletarAsync(rawBody, sessao, cliente, ct);
        var normalizado = WppConnectWebhookParser.Parse(rawBody, telefone, sessao.InicioDaCaptura);
        // #3117: aceito e sem o que gravar. Responde 200 como antes, mas agora diz por quê.
        if (normalizado.Descarte is { } motivo)
            WebhookSemEfeito.Registrar((ILogger?)logger ?? NullLogger.Instance, metricas, motivo, evento, nome);
        // #2515: o "2" que responde ao menu numerado volta como o toque no botão.
        if (menus is not null)
            normalizado = await EscolhaMenuWppConnect.TraduzirAsync(normalizado, sessao.Sessao, menus, ct);
        return await processar.ExecuteNormalizadoAsync(normalizado, empresaId, ct);
    }
}

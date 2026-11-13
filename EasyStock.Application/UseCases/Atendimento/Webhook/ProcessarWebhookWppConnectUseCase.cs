using System.Text.Json;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Integration.Crypto;
using EasyStock.Domain.Integration;

namespace EasyStock.Application.UseCases.Atendimento.Webhook;

public sealed class ProcessarWebhookWppConnectUseCase(
    IIntegrationCredentialResolver credenciais, ITenantContextAccessor tenant,
    IWppConnectSessaoClient cliente, ProcessarEventoWhatsAppUseCase processar)
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
        var normalizado = WppConnectWebhookParser.Parse(rawBody, telefone, sessao.InicioDaCaptura);
        return await processar.ExecuteNormalizadoAsync(normalizado, empresaId, ct);
    }
}

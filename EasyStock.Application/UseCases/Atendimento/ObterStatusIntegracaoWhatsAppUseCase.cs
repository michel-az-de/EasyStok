using EasyStock.Application.UseCases.FeatureFlags;

namespace EasyStock.Application.UseCases.Atendimento;

public sealed record ObterStatusIntegracaoWhatsAppQuery(Guid EmpresaId);

/// <summary>
/// WebhookVerificadoEm e UltimaMensagemRecebidaEm ainda nao tem onde ser gravados (S03): sempre
/// nulos ate la, como a spec de S01 prevê.
/// </summary>
public sealed record StatusIntegracaoWhatsAppDto(
    string? PhoneNumberId,
    DateTime? WebhookVerificadoEm,
    DateTime? UltimaMensagemRecebidaEm);

/// <summary>
/// Devolve null quando o tenant nao tem o modulo de atendimento ligado (ADR-0048) — o controller
/// traduz para 404, em vez de vazar detalhe de integracao pra quem nao contratou o modulo.
/// </summary>
public sealed class ObterStatusIntegracaoWhatsAppUseCase(
    ITenantFeatureFlagRepository featureFlagRepository,
    IEmpresaRepository empresaRepository)
{
    public async Task<StatusIntegracaoWhatsAppDto?> ExecuteAsync(
        ObterStatusIntegracaoWhatsAppQuery query, CancellationToken ct = default)
    {
        var ativas = await featureFlagRepository.ListarAtivasAsync(query.EmpresaId, ct);
        if (!ativas.Contains(FeatureCatalogo.ModuloAtendimento, StringComparer.OrdinalIgnoreCase))
            return null;

        var empresa = await empresaRepository.GetByIdAsync(query.EmpresaId);
        return new StatusIntegracaoWhatsAppDto(
            PhoneNumberId: empresa?.WhatsAppPhoneNumberId,
            WebhookVerificadoEm: null,
            UltimaMensagemRecebidaEm: null);
    }
}

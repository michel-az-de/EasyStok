using EasyStock.Application.UseCases.FeatureFlags;

namespace EasyStock.Application.UseCases.Atendimento;

public sealed record ObterStatusIntegracaoWhatsAppQuery(Guid EmpresaId);

/// <summary>
/// Os dois carimbos vêm da <c>ConfiguracaoAtendimento</c> da empresa (F16, #1246).
/// <c>UltimaMensagemRecebidaEm</c> é gravado pelo webhook a cada mensagem. <c>WebhookVerificadoEm</c>
/// continua nulo na prática: a verificação da Meta (<c>hub.challenge</c>) é por app, não por
/// empresa, e nada grava o campo (registrado na #1246).
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
    IEmpresaRepository empresaRepository,
    IConfiguracaoAtendimentoRepository configuracaoRepository)
{
    public async Task<StatusIntegracaoWhatsAppDto?> ExecuteAsync(
        ObterStatusIntegracaoWhatsAppQuery query, CancellationToken ct = default)
    {
        var ativas = await featureFlagRepository.ListarAtivasAsync(query.EmpresaId, ct);
        if (!ativas.Contains(FeatureCatalogo.ModuloAtendimento, StringComparer.OrdinalIgnoreCase))
            return null;

        var empresa = await empresaRepository.GetByIdAsync(query.EmpresaId);
        var configuracao = await configuracaoRepository.GetByEmpresaIdAsync(query.EmpresaId);
        return new StatusIntegracaoWhatsAppDto(
            PhoneNumberId: empresa?.WhatsAppPhoneNumberId,
            WebhookVerificadoEm: configuracao?.WebhookVerificadoEm,
            UltimaMensagemRecebidaEm: configuracao?.UltimaMensagemRecebidaEm);
    }
}

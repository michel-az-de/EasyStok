using System.Diagnostics.Metrics;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using Microsoft.Extensions.Configuration;

namespace EasyStock.Application.UseCases.Notifications.Plataforma;

/// <summary>
/// Status do número de plataforma (N6). Acha a linha do outbox pelo opaco <c>{EmpresaId:N}.{OutboxId:N}</c> que o envio
/// levou (<c>biz_opaque_callback_data</c>), fixa o tenant dessa empresa (sem bypass de RLS e sem depender do
/// <c>wamid</c>, que não existe numa entrega <c>Indeterminado</c>) e aplica a transição monotônica: <c>sent</c>,
/// <c>delivered</c> e <c>read</c> fecham <c>EmEnvio</c> ou <c>Indeterminado</c> como <c>Enviado</c>; <c>failed</c> fecha
/// <c>Enviado</c> ou <c>Indeterminado</c> como <c>Falhado</c>. A Meta reenvia por até 7 dias e o <c>delivered</c> pode
/// faltar: status repetido ou fora de ordem não regride. O log nunca leva telefone.
/// </summary>
public sealed class ProcessarStatusWhatsAppPlataformaUseCase(
    IOutboxNotificacaoRepository outboxRepository,
    ITenantContextAccessor tenantContext,
    IUnitOfWork unitOfWork,
    IConfiguration configuration,
    ILogger<ProcessarStatusWhatsAppPlataformaUseCase> logger)
{
    private static readonly Meter Medidor = new("EasyStock.Notifications", "1.0");

    private static readonly Counter<long> SemOpaco = Medidor.CreateCounter<long>(
        "notifications.whatsapp.plataforma.status_sem_opaco", "status",
        "Status do número de plataforma sem o opaco que o envio levou (a Meta deixou de devolvê-lo?)");

    private static readonly Counter<long> CobradoComoMarketing = Medidor.CreateCounter<long>(
        "notifications.whatsapp.plataforma.cobrado_como_marketing", "status",
        "Status de template de plataforma cobrado como marketing pela Meta");

    private const string Provider = "meta-plataforma";

    /// <returns><c>false</c> só em falha transitória (banco): o controller pede à Meta que reenvie.</returns>
    public async Task<bool> ExecuteAsync(StatusPlataforma status, CancellationToken ct = default)
    {
        var plataforma = configuration["Notifications:WhatsApp:Plataforma:PhoneNumberId"]?.Trim();
        if (string.IsNullOrEmpty(plataforma) || status.PhoneNumberId != plataforma)
        {
            logger.LogWarning(
                "Webhook de plataforma: status de outro número ignorado (phone_number_id {PhoneNumberId}).", status.PhoneNumberId);
            return true;
        }

        if (!TentarLerOpaco(status.Opaco, out var empresaId, out var outboxId))
        {
            SemOpaco.Add(1);
            logger.LogWarning("Webhook de plataforma: status {Status} sem opaco, ignorado (wamid {Wamid}).", status.Status, status.Wamid);
            return true;
        }

        try
        {
            tenantContext.SetCurrentTenant(empresaId);
            var mensagem = await outboxRepository.ObterAsync(empresaId, outboxId, ct);
            if (mensagem is null || mensagem.Canal != CanalNotificacao.WhatsApp || mensagem.Remetente != OrigemRemetente.Plataforma)
            {
                logger.LogWarning(
                    "Webhook de plataforma: opaco sem mensagem de plataforma correspondente, ignorado (wamid {Wamid}).", status.Wamid);
                return true;
            }

            if (string.Equals(status.CategoriaPreco, "marketing", StringComparison.OrdinalIgnoreCase))
            {
                CobradoComoMarketing.Add(1);
                var template = mensagem.LerMetadados() is { } md && md.TryGetValue("template", out var t) ? t : "(purgado)";
                logger.LogWarning(
                    "Webhook de plataforma: template {Template} cobrado como marketing pela Meta (outbox {OutboxId}).",
                    template, mensagem.Id);
            }

            if (Aplicar(mensagem, status))
                await unitOfWork.CommitAsync();
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Webhook de plataforma: falha transitória ao fechar o status {Status} (outbox {OutboxId}).",
                status.Status, outboxId);
            return false;
        }
    }

    private bool Aplicar(OutboxMensagemNotificacao mensagem, StatusPlataforma status)
    {
        switch (status.Status.ToLowerInvariant())
        {
            case "sent" or "delivered" or "read":
                return mensagem.ConfirmarEnvioPeloProvider(status.Wamid, Provider);

            case "failed":
            {
                var classe = status.CodigoErro is { } c ? CodigosErroMeta.Classificar(c).ToString() : "sem_codigo";
                var erro = $"meta_{status.CodigoErro?.ToString() ?? "sem_codigo"} ({classe}): {status.MensagemErro}";
                var mudou = mensagem.RegistrarFalhaDeEntregaPeloProvider(erro);
                if (mudou)
                    logger.LogWarning(
                        "Webhook de plataforma: entrega falhou com {Codigo} classe {Classe} (outbox {OutboxId}).",
                        status.CodigoErro, classe, mensagem.Id);
                return mudou;
            }

            default:
                // accepted, deleted, warning: nada a fechar.
                return false;
        }
    }

    private static bool TentarLerOpaco(string? opaco, out Guid empresaId, out Guid outboxId)
    {
        empresaId = outboxId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(opaco)) return false;
        var partes = opaco.Split('.');
        return partes.Length == 2
            && Guid.TryParseExact(partes[0], "N", out empresaId)
            && Guid.TryParseExact(partes[1], "N", out outboxId);
    }
}

using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace EasyStock.Application.UseCases.Notifications.Plataforma;

/// <summary>
/// Webhook do número de plataforma (N6): a Meta entrega aqui, pelo override do número, os <c>statuses</c> e as
/// <c>messages</c> dele. Só o <c>phone_number_id</c> da plataforma é processado; outro é ignorado e logado.
/// </summary>
public sealed class ProcessarWebhookWhatsAppPlataformaUseCase(
    ProcessarStatusWhatsAppPlataformaUseCase status,
    ResponderMensagemRecebidaPlataformaUseCase responder,
    IConfiguration configuration,
    ILogger<ProcessarWebhookWhatsAppPlataformaUseCase> logger)
{
    /// <returns><c>false</c> só em falha transitória: o controller responde 503 e a Meta reenvia.</returns>
    public async Task<bool> ExecuteAsync(string rawBody, CancellationToken ct = default)
    {
        EventoWebhookPlataforma evento;
        try
        {
            evento = WebhookPlataformaParser.Parse(rawBody);
        }
        catch (JsonException)
        {
            logger.LogWarning("Webhook de plataforma: corpo não é JSON, ignorado.");
            return true;
        }

        var completo = true;
        foreach (var s in evento.Statuses)
            completo &= await status.ExecuteAsync(s, ct);

        var plataforma = configuration["Notifications:WhatsApp:Plataforma:PhoneNumberId"]?.Trim();
        foreach (var m in evento.Mensagens)
        {
            if (string.IsNullOrEmpty(plataforma) || m.PhoneNumberId != plataforma)
            {
                logger.LogWarning("Webhook de plataforma: mensagem de outro número ignorada (phone_number_id {PhoneNumberId}).", m.PhoneNumberId);
                continue;
            }

            try
            {
                await responder.ExecuteAsync(m, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A resposta automática é cortesia: falhar nela não vale pedir à Meta que reenvie o lote inteiro.
                logger.LogError(ex, "Webhook de plataforma: resposta automática falhou.");
            }
        }

        return completo;
    }
}

using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Notifications.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebPush;
using WebPushClient = WebPush.WebPushClient;

namespace EasyStock.Infra.Notifications.Push;

/// <summary>
/// Onda 2.2 — canal de Web Push via VAPID (PWA). Resolve subscriptions ativas
/// para o destinatario (UsuarioId ou EmpresaId), envia payload criptografado
/// ECDH P-256, e desativa subscription quando o push service retorna 410 Gone.
///
/// <para>
/// Limitacao do <see cref="MensagemPronta"/>: o campo Destinatario eh string
/// (espelha email/telefone). Para Push, usamos a convencao "usuario:{guid}" ou
/// "empresa:{guid}" — o canal parseia e busca subscriptions correspondentes.
/// Se nenhuma subscription ativa for encontrada, retorna sucesso=false com
/// ErroDetalhado="NENHUMA_SUBSCRIPTION_ATIVA" (NotificadorService trata como
/// nao-bloqueio: usuario simplesmente nao tem PWA registrado).
/// </para>
/// </summary>
public sealed class WebPushCanal(
    IWebPushSubscriptionRepository repo,
    IOptions<WebPushOptions> options,
    ILogger<WebPushCanal> logger) : ICanalNotificacao
{
    public CanalNotificacao Canal => CanalNotificacao.Push;

    private readonly WebPushOptions _opts = options.Value;
    private readonly WebPushClient _client = new();

    public async Task<ResultadoEnvio> EnviarAsync(MensagemPronta mensagem, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        if (string.IsNullOrWhiteSpace(_opts.PublicKey) || string.IsNullOrWhiteSpace(_opts.PrivateKey))
        {
            return new ResultadoEnvio(false, "webpush", "WebPush:PublicKey/PrivateKey nao configurados.", DuracaoMs: sw.ElapsedMilliseconds);
        }

        var subs = await ResolverSubscriptionsAsync(mensagem, ct);
        if (subs.Count == 0)
        {
            sw.Stop();
            return new ResultadoEnvio(false, "webpush", "NENHUMA_SUBSCRIPTION_ATIVA", DuracaoMs: sw.ElapsedMilliseconds);
        }

        var vapid = new VapidDetails(_opts.Subject, _opts.PublicKey, _opts.PrivateKey);
        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            title = mensagem.Assunto,
            body = mensagem.Corpo,
            tag = mensagem.OutboxId.ToString(),
            data = new { outboxId = mensagem.OutboxId, empresaId = mensagem.EmpresaId }
        });

        var sucessos = 0;
        var falhas = 0;
        foreach (var sub in subs)
        {
            try
            {
                var psub = new PushSubscription(sub.Endpoint, sub.P256dh, sub.Auth);
                await _client.SendNotificationAsync(psub, payload, vapid);
                sub.MarcarUso();
                await repo.UpdateAsync(sub, ct);
                sucessos++;
            }
            catch (WebPushException ex) when ((int)ex.StatusCode == 410 || (int)ex.StatusCode == 404)
            {
                // 410 Gone = subscription expirou. 404 = endpoint nao existe mais. Desativa para parar de tentar.
                logger.LogInformation("Subscription Web Push {Endpoint} desativada (HTTP {Status}).", sub.Endpoint, (int)ex.StatusCode);
                await repo.DesativarAsync(sub.Endpoint, ct);
                falhas++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Falha ao enviar Web Push para subscription {Endpoint}", sub.Endpoint);
                falhas++;
            }
        }

        sw.Stop();
        return new ResultadoEnvio(
            Sucesso: sucessos > 0,
            ProviderUsado: "webpush",
            ErroDetalhado: sucessos > 0 ? null : $"todas {falhas} subs falharam",
            DuracaoMs: sw.ElapsedMilliseconds);
    }

    private async Task<IReadOnlyList<Domain.Entities.Notifications.WebPushSubscription>> ResolverSubscriptionsAsync(MensagemPronta msg, CancellationToken ct)
    {
        var dest = msg.Destinatario.Trim();
        if (dest.StartsWith("usuario:", StringComparison.OrdinalIgnoreCase) && Guid.TryParse(dest[8..], out var usuarioId))
            return await repo.GetByUsuarioAsync(usuarioId, ct);
        if (dest.StartsWith("empresa:", StringComparison.OrdinalIgnoreCase) && Guid.TryParse(dest[8..], out var empId))
            return await repo.GetByEmpresaAsync(empId, ct);
        // Fallback: empresa da mensagem.
        return await repo.GetByEmpresaAsync(msg.EmpresaId, ct);
    }
}

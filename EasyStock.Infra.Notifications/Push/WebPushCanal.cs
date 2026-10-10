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
    ILogger<WebPushCanal> logger,
    WebPushClient? client = null,
    IHttpClientFactory? httpClientFactory = null) : ICanalNotificacao, IDisposable
{
    /// <summary>Nome do HttpClient registrado com <c>AddHttpClient</c> para o push service.</summary>
    public const string NomeHttpClient = "WebPush";

    public CanalNotificacao Canal => CanalNotificacao.Push;

    private readonly WebPushOptions _opts = options.Value;

    // O WebPushClient e injetavel so para teste (HttpClient com handler falso); o container nao o registra. Em runtime
    // o HttpClient vem da fabrica (#1507): o canal e scoped e um WebPushClient() proprio criava um HttpClient por escopo
    // que nunca era descartado. Sem fabrica, o cliente proprio e descartado junto com o escopo (Dispose).
    private readonly WebPushClient _client = client
        ?? (httpClientFactory is not null ? new WebPushClient(httpClientFactory.CreateClient(NomeHttpClient)) : new());

    public void Dispose()
    {
        // So descarta o que o canal criou; o HttpClient da fabrica nao e do WebPushClient e nao e fechado aqui.
        if (client is null) _client.Dispose();
    }

    public async Task<ResultadoEnvio> EnviarAsync(MensagemPronta mensagem, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Sem chave VAPID nenhuma tentativa vai passar: falha permanente (N2).
        if (string.IsNullOrWhiteSpace(_opts.PublicKey) || string.IsNullOrWhiteSpace(_opts.PrivateKey))
        {
            return new ResultadoEnvio(false, "webpush", "WebPush:PublicKey/PrivateKey nao configurados.",
                DuracaoMs: sw.ElapsedMilliseconds, FalhaPermanente: true);
        }

        var subs = (await ResolverSubscriptionsAsync(mensagem, ct))
            .Where(s => s.EmpresaId == mensagem.EmpresaId && s.Ativo).ToList();
        if (subs.Count == 0)
        {
            sw.Stop();
            // Ninguem registrou o PWA: repetir nao cria inscricao, falha permanente (N2).
            return new ResultadoEnvio(false, "webpush", "NENHUMA_SUBSCRIPTION_ATIVA",
                DuracaoMs: sw.ElapsedMilliseconds, FalhaPermanente: true);
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
        var permanentes = 0;
        foreach (var sub in subs)
        {
            // #1508: inscricao gravada antes da allowlist nao pode virar SSRF. Desativa e conta como permanente.
            if (!EndpointPushPermitido.Valido(sub.Endpoint))
            {
                logger.LogWarning("Subscription Web Push {SubscriptionId} com endpoint fora da allowlist desativada.", sub.Id);
                await repo.DesativarAsync(sub.Endpoint, ct);
                falhas++;
                permanentes++;
                continue;
            }

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
                permanentes++;
            }
            catch (WebPushException ex)
            {
                // Resposta do push service (N2): 4xx, menos 408 e 429 (400, 401, 403 e 413 entre eles), nunca passa;
                // 5xx, 408 e 429 passam.
                logger.LogWarning(ex, "Falha ao enviar Web Push para subscription {Endpoint} (HTTP {Status})",
                    sub.Endpoint, (int)ex.StatusCode);
                falhas++;
                if (ClassificadorDeFalha.HttpEhPermanente((int)ex.StatusCode)) permanentes++;
            }
            catch (Exception ex)
            {
                // Rede e o resto: transitorio.
                logger.LogWarning(ex, "Falha ao enviar Web Push para subscription {Endpoint}", sub.Endpoint);
                falhas++;
            }
        }

        sw.Stop();
        return new ResultadoEnvio(
            Sucesso: sucessos > 0,
            ProviderUsado: "webpush",
            ErroDetalhado: sucessos > 0 ? null : $"todas {falhas} subs falharam",
            DuracaoMs: sw.ElapsedMilliseconds,
            // Sem nenhuma entrega e com toda falha permanente, repetir nao adianta. Com ao menos uma falha
            // transitoria o outbox tenta de novo, e nenhuma inscricao recebeu, entao nao duplica.
            FalhaPermanente: sucessos == 0 && permanentes == falhas);
    }

    private async Task<IReadOnlyList<Domain.Entities.Notifications.WebPushSubscription>> ResolverSubscriptionsAsync(MensagemPronta msg, CancellationToken ct)
    {
        var dest = msg.Destinatario.Trim();
        if (dest.StartsWith("usuario:", StringComparison.OrdinalIgnoreCase) && Guid.TryParse(dest[8..], out var usuarioId))
            return (await repo.GetByUsuarioAsync(usuarioId, ct)).Where(s => s.UsuarioId == usuarioId).ToList();
        if (dest.StartsWith("empresa:", StringComparison.OrdinalIgnoreCase) && Guid.TryParse(dest[8..], out var empId))
            return empId == msg.EmpresaId ? await repo.GetByEmpresaAsync(empId, ct) : [];
        // Fallback: empresa da mensagem.
        return await repo.GetByEmpresaAsync(msg.EmpresaId, ct);
    }
}

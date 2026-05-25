using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Infra.Notifications.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace EasyStock.Api.Controllers;

/// <summary>
/// Onda 2.2 — gerenciamento de subscriptions Web Push do PWA. Cliente autentica
/// via JWT (cookie/header), pede permission do browser, chama PushManager.subscribe
/// com a VAPID public key, e POSTa o resultado aqui. Backend usa esses dados pra
/// enviar push criptografado depois.
/// </summary>
[ApiController]
[Route("api/pwa/push")]
[Authorize]
public class PwaPushController(
    IWebPushSubscriptionRepository repo,
    ICurrentUserAccessor currentUser,
    IOptions<WebPushOptions> opts) : EasyStockControllerBase
{
    public sealed record SubscribeRequest(string Endpoint, string P256dh, string Auth, string? UserAgent);
    public sealed record VapidPublicKeyResult(string PublicKey, string Subject);

    /// <summary>
    /// Retorna a chave publica VAPID — usada pelo PWA em PushManager.subscribe().
    /// Endpoint anonimo: a public key nao eh segredo (private key sim).
    /// </summary>
    [HttpGet("vapid-public")]
    [AllowAnonymous]
    public IActionResult GetVapidPublic()
    {
        var o = opts.Value;
        if (string.IsNullOrWhiteSpace(o.PublicKey))
            return DataNotFound("WebPush nao configurado neste ambiente.");
        return DataOk(new VapidPublicKeyResult(o.PublicKey, o.Subject));
    }

    /// <summary>
    /// Registra (ou atualiza) uma subscription do browser autenticado.
    /// Idempotente: se endpoint ja existe, atualiza P256dh/Auth/UltimoUso/UserAgent.
    /// </summary>
    [HttpPost("subscribe")]
    public async Task<IActionResult> Subscribe([FromBody] SubscribeRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Endpoint)
            || string.IsNullOrWhiteSpace(req.P256dh)
            || string.IsNullOrWhiteSpace(req.Auth))
            return DataBadRequest("Endpoint, P256dh e Auth sao obrigatorios.");

        var existing = await repo.GetByEndpointAsync(req.Endpoint, ct);
        if (existing is not null)
        {
            existing.P256dh = req.P256dh;
            existing.Auth = req.Auth;
            existing.UserAgent = req.UserAgent ?? existing.UserAgent;
            existing.Ativo = true;
            existing.MarcarUso();
            // Re-vincula a empresa/usuario atual (pode ter mudado de conta no mesmo browser).
            existing.EmpresaId = currentUser.EmpresaId == Guid.Empty ? null : currentUser.EmpresaId;
            existing.UsuarioId = currentUser.UsuarioId == Guid.Empty ? null : currentUser.UsuarioId;
            await repo.UpdateAsync(existing, ct);
            return DataOk(new { id = existing.Id, atualizado = true });
        }

        var sub = WebPushSubscription.Criar(
            endpoint: req.Endpoint,
            p256dh: req.P256dh,
            auth: req.Auth,
            empresaId: currentUser.EmpresaId == Guid.Empty ? null : currentUser.EmpresaId,
            usuarioId: currentUser.UsuarioId == Guid.Empty ? null : currentUser.UsuarioId,
            userAgent: req.UserAgent);
        await repo.AddAsync(sub, ct);
        return DataCreated($"/api/pwa/push/{sub.Id}", new { id = sub.Id, atualizado = false });
    }

    /// <summary>Desativa a subscription do endpoint (cliente desabilitou push ou desinstalou PWA).</summary>
    [HttpDelete("unsubscribe")]
    public async Task<IActionResult> Unsubscribe([FromQuery] string endpoint, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) return DataBadRequest("Endpoint obrigatorio.");
        await repo.DesativarAsync(endpoint, ct);
        return NoContent();
    }
}

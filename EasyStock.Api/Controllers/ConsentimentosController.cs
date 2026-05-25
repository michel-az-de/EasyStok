using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.UseCases.Notifications;
using EasyStock.Domain.Enums.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text;

namespace EasyStock.Api.Controllers;

[ApiController]
[Route("api/consentimentos")]
public sealed class ConsentimentosController(
    ICurrentUserAccessor currentUser,
    IConsentimentoRepository repo,
    RegistrarOptInUseCase optInUseCase,
    RegistrarOptOutUseCase optOutUseCase,
    IConfiguration configuration) : EasyStockControllerBase
{
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var items = await repo.ListarPorUsuarioAsync(currentUser.UsuarioId, ct);
        var dto = items.Select(c => new
        {
            canal = c.Canal.ToString(),
            categoria = c.Categoria.ToString(),
            optIn = c.OptIn,
            atualizadoEm = c.AtualizadoEm
        });
        return DataOk(dto);
    }

    [HttpPost("opt-in")]
    [Authorize]
    public async Task<IActionResult> OptIn([FromBody] ConsentimentoRequest req)
    {
        await optInUseCase.ExecuteAsync(new RegistrarOptInCommand(
            currentUser.UsuarioId, req.Canal, req.Categoria,
            currentUser.UsuarioId.ToString()));
        return DataOk(new { registrado = true });
    }

    [HttpPost("opt-out")]
    [Authorize]
    public async Task<IActionResult> OptOut([FromBody] ConsentimentoRequest req)
    {
        await optOutUseCase.ExecuteAsync(new RegistrarOptOutCommand(
            currentUser.UsuarioId, req.Canal, req.Categoria,
            currentUser.UsuarioId.ToString(), req.Motivo));
        return DataOk(new { registrado = true });
    }

    /// <summary>
    /// Endpoint público para descadastro 1-clique via link no rodapé de emails.
    /// Token format: {base64url(uid:canal:cat)}.{hmac_hex_first32}
    /// </summary>
    [HttpGet("unsubscribe")]
    [AllowAnonymous]
    public async Task<IActionResult> Unsubscribe([FromQuery] string t, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(t))
            return BadRequest("Token inválido.");

        var parts = t.Split('.');
        if (parts.Length != 2)
            return BadRequest("Token inválido.");

        Guid usuarioId;
        CanalNotificacao canal;
        CategoriaConteudoNotificacao categoria;

        try
        {
            var payload = Encoding.UTF8.GetString(Base64UrlDecode(parts[0]));
            var secret = configuration["Notifications:UnsubscribeSecret"]
                ?? configuration["JwtSettings:SecretKey"]
                ?? "default-unsubscribe-secret-change-me";

            var expectedHmac = ComputeHmac(secret, payload)[..32];
            if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(parts[1]),
                Encoding.ASCII.GetBytes(expectedHmac)))
            {
                return BadRequest("Token inválido.");
            }

            var fields = payload.Split(':');
            if (fields.Length != 3 ||
                !Guid.TryParse(fields[0], out usuarioId) ||
                !Enum.TryParse<CanalNotificacao>(fields[1], out canal) ||
                !Enum.TryParse<CategoriaConteudoNotificacao>(fields[2], out categoria))
            {
                return BadRequest("Token inválido.");
            }
        }
        catch
        {
            return BadRequest("Token inválido.");
        }

        await optOutUseCase.ExecuteAsync(new RegistrarOptOutCommand(
            usuarioId, canal, categoria,
            "unsubscribe-link", "Descadastro via link no email"));

        return Ok(new { mensagem = "Você foi descadastrado com sucesso. Suas preferências foram atualizadas." });
    }

    /// <summary>
    /// Gera token HMAC para link de unsubscribe 1-clique.
    /// Chamado internamente ao renderizar templates de email.
    /// </summary>
    public static string GerarToken(string secret, Guid usuarioId, CanalNotificacao canal, CategoriaConteudoNotificacao categoria)
    {
        var payload = $"{usuarioId}:{canal}:{categoria}";
        var hmac = ComputeHmac(secret, payload)[..32];
        return $"{Base64UrlEncode(Encoding.UTF8.GetBytes(payload))}.{hmac}";
    }

    private static string ComputeHmac(string secret, string message)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var msgBytes = Encoding.UTF8.GetBytes(message);
        var hash = HMACSHA256.HashData(keyBytes, msgBytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static byte[] Base64UrlDecode(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
        return Convert.FromBase64String(s);
    }

    public record ConsentimentoRequest(
        CanalNotificacao Canal,
        CategoriaConteudoNotificacao Categoria,
        string? Motivo = null);
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EasyStock.Api.Http;
using EasyStock.Application.UseCases.Storefront.Menu;
using EasyStock.Domain.Exceptions.Storefront;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers.Storefront;

/// <summary>
/// Endpoint público do cardápio do storefront (ADR-0012 / TASK-EZ-MENU-001).
///
/// <para>
/// <strong>Anônimo</strong>: cliente storefront NÃO está autenticado nesta fase.
/// Tenant vem do slug na rota. CORS permite a origem pública (configurado em
/// <c>AddEasyStockCors</c>).
/// </para>
///
/// <para>
/// <strong>Cache HTTP</strong>: <c>Cache-Control: public, max-age=300, s-maxage=300</c>
/// (5 min em browser + edge). Cloudflare cacheia na borda. Invalidação cross-edge é
/// eventual — aceitável pro MVP. ETag SHA-256 do payload permite <c>304 Not Modified</c>
/// quando o cliente envia <c>If-None-Match</c>.
/// </para>
///
/// <para>
/// <strong>Storefront inativo</strong> retorna 404 (não 403) para não vazar
/// existência do tenant.
/// </para>
/// </summary>
[SwaggerTag("Storefront Menu / Cardápio público do storefront")]
[ApiController]
[Route("api/storefront/{slug}/menu")]
[AllowAnonymous]
public sealed class MenuController(
    ListarCardapioPublicoUseCase listarCardapioUseCase,
    ILogger<MenuController> logger) : EasyStockControllerBase
{
    /// <summary>
    /// JSON options dedicado ao endpoint público — camelCase + null skip. Estático
    /// pra garantir que o ETag (hash do payload) seja determinístico requisição
    /// após requisição.
    /// </summary>
    private static readonly JsonSerializerOptions PublicJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false,
    };

    private const string CacheControlPublico = "public, max-age=300, s-maxage=300";

    /// <summary>
    /// Lista os items visíveis do cardápio do storefront identificado por
    /// <paramref name="slug"/>. Ordenação: categoria ASC → ordemExibicao ASC.
    /// </summary>
    [SwaggerOperation(
        Summary = "Cardápio público do storefront",
        Description = "Endpoint anônimo. Retorna apenas items com Visivel=true. Cache HTTP 5min (browser + edge). " +
                      "Suporta ETag/If-None-Match para 304 Not Modified.")]
    [ProducesResponseType(typeof(IReadOnlyList<CardapioItemPublicoDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromRoute] string slug,
        CancellationToken ct)
    {
        try
        {
            var result = await listarCardapioUseCase.ExecuteAsync(
                new ListarCardapioPublicoInput(slug), ct);

            var json = JsonSerializer.SerializeToUtf8Bytes(result.Itens, PublicJsonOptions);
            var etag = ComputarETag(json);

            // Sempre seta cache-headers, mesmo no 304 (proxies precisam pra TTL).
            Response.Headers[HeaderNames.CacheControl] = CacheControlPublico;
            Response.Headers[HeaderNames.ETag] = etag;

            if (ClienteJaTemPayload(etag))
                return StatusCode(StatusCodes.Status304NotModified);

            return File(json, "application/json; charset=utf-8");
        }
        catch (StorefrontNaoEncontradoException)
        {
            logger.LogInformation("Cardápio público solicitado para slug inexistente/inativo: slug={Slug}", slug);
            return Problem(
                type: "https://easystok.app/errors/storefront-not-found",
                title: "Storefront não encontrado",
                detail: $"Nenhum storefront ativo corresponde ao slug informado.",
                statusCode: StatusCodes.Status404NotFound);
        }
    }

    /// <summary>ETag strong: SHA-256 do payload em hex, entre aspas (RFC 7232).</summary>
    private static string ComputarETag(ReadOnlySpan<byte> payload)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(payload, hash);
        return $"\"{Convert.ToHexString(hash)}\"";
    }

    private bool ClienteJaTemPayload(string etag)
    {
        if (!Request.Headers.TryGetValue(HeaderNames.IfNoneMatch, out var values))
            return false;

        foreach (var raw in values)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            // Compara cada token separado por vírgula (RFC 7232) — ignora prefixo W/
            // (weak vs strong não importa pra hash determinístico do payload).
            foreach (var token in raw.Split(','))
            {
                var normalizado = token.Trim();
                if (normalizado.StartsWith("W/", StringComparison.Ordinal))
                    normalizado = normalizado[2..];
                if (string.Equals(normalizado, etag, StringComparison.Ordinal) || normalizado == "*")
                    return true;
            }
        }
        return false;
    }
}

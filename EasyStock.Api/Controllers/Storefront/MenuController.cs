using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EasyStock.Application.Common;
using EasyStock.Application.UseCases.Storefront.Menu;
using EasyStock.Domain.Exceptions.Storefront;
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

    /// <summary>
    /// Versão imprimível do cardápio (HTML com media-print). Anônimo,
    /// resolve o storefront pelo mesmo <paramref name="slug"/> do endpoint JSON.
    /// <c>Cache-Control: no-store</c> porque carimba data/hora local (BRT) — não cacheável.
    /// </summary>
    [SwaggerOperation(
        Summary = "Cardápio do storefront em HTML imprimível",
        Description = "Endpoint anônimo. HTML pronto para impressão. Data/hora em horário de Brasília.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [HttpGet("imprimir")]
    public async Task<IActionResult> Imprimir([FromRoute] string slug, CancellationToken ct)
    {
        try
        {
            var result = await listarCardapioUseCase.ExecuteAsync(
                new ListarCardapioPublicoInput(slug), ct);

            Response.Headers[HeaderNames.CacheControl] = "no-store";
            var html = RenderizarHtmlImpressao(result);
            return Content(html, "text/html; charset=utf-8");
        }
        catch (StorefrontNaoEncontradoException)
        {
            logger.LogInformation("Impressão de cardápio solicitada para slug inexistente/inativo: slug={Slug}", slug);
            return Problem(
                type: "https://easystok.app/errors/storefront-not-found",
                title: "Storefront não encontrado",
                detail: "Nenhum storefront ativo corresponde ao slug informado.",
                statusCode: StatusCodes.Status404NotFound);
        }
    }

    /// <summary>
    /// Monta o HTML de impressão. Agrupa por categoria (ordem já vem do use case).
    /// Todo texto de tenant é HTML-encoded (anti-XSS no HTML servido).
    /// </summary>
    private static string RenderizarHtmlImpressao(ListarCardapioPublicoResult result)
    {
        static string E(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);
        static string Preco(long centavos) => "R$ " + (centavos / 100m).ToString("F2",
            System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));

        var titulo = string.IsNullOrWhiteSpace(result.TituloPublico) ? result.Slug : result.TituloPublico;
        var carimbo = HorarioBrasil.Agora().ToString("dd/MM/yyyy HH:mm",
            System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));

        var sb = new StringBuilder(4096);
        sb.Append("""
<!DOCTYPE html>
<html lang="pt-BR"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Cardápio
""");
        sb.Append(" — ").Append(E(titulo)).Append("""
</title>
<style>
  * { box-sizing: border-box; }
  body { font-family: Georgia, 'Times New Roman', serif; color: #2b2b2b; max-width: 760px; margin: 0 auto; padding: 32px 24px; }
  header { text-align: center; border-bottom: 2px solid #b84a2c; padding-bottom: 16px; margin-bottom: 24px; }
  h1 { margin: 0 0 4px; font-size: 28px; color: #5c2e0d; }
  .carimbo { font-size: 11px; color: #999; }
  .categoria { font-size: 18px; color: #b84a2c; border-bottom: 1px solid #e5d5c0; padding-bottom: 4px; margin: 24px 0 12px; }
  .item { display: flex; justify-content: space-between; align-items: baseline; gap: 12px; margin: 0 0 10px; page-break-inside: avoid; }
  .item-nome { font-weight: bold; }
  .item-desc { font-size: 13px; color: #666; font-style: italic; }
  .item-preco { white-space: nowrap; font-weight: bold; color: #5c2e0d; }
  .esgotado { opacity: .5; }
  .esgotado-tag { font-size: 11px; color: #c03b2a; font-style: normal; }
  footer { margin-top: 32px; text-align: center; font-size: 11px; color: #aaa; border-top: 1px solid #eee; padding-top: 12px; }
  @media print {
    body { padding: 0; }
    .no-print { display: none; }
    @page { margin: 1.5cm; }
  }
  .btn-print { display:inline-block; margin-top:8px; padding:6px 16px; background:#b84a2c; color:#fff; border:none; border-radius:6px; cursor:pointer; font-size:13px; }
</style></head><body>
<header>
""");
        sb.Append("<h1>").Append(E(titulo)).Append("</h1>");
        sb.Append("<div class=\"carimbo\">Cardápio impresso em ").Append(E(carimbo)).Append("</div>");
        sb.Append("<button class=\"btn-print no-print\" onclick=\"window.print()\">Imprimir</button>");
        sb.Append("</header>");

        if (result.Itens.Count == 0)
        {
            sb.Append("<p style=\"text-align:center;color:#999;\">Cardápio em atualização.</p>");
        }
        else
        {
            // Itens já vêm ordenados por categoria → ordem. Agrupa preservando a ordem.
            string? categoriaAtual = null;
            foreach (var item in result.Itens)
            {
                var cat = string.IsNullOrWhiteSpace(item.Categoria) ? "Outros" : item.Categoria!;
                if (!string.Equals(cat, categoriaAtual, StringComparison.Ordinal))
                {
                    categoriaAtual = cat;
                    sb.Append("<div class=\"categoria\">").Append(E(cat)).Append("</div>");
                }

                var esgotado = !item.Disponivel;
                sb.Append("<div class=\"item").Append(esgotado ? " esgotado" : "").Append("\">");
                sb.Append("<div><span class=\"item-nome\">").Append(E(item.Nome)).Append("</span>");
                if (esgotado)
                    sb.Append(" <span class=\"esgotado-tag\">(esgotado)</span>");
                if (!string.IsNullOrWhiteSpace(item.Descricao))
                    sb.Append("<div class=\"item-desc\">").Append(E(item.Descricao)).Append("</div>");
                sb.Append("</div>");
                sb.Append("<span class=\"item-preco\">").Append(E(Preco(item.PrecoCentavos))).Append("</span>");
                sb.Append("</div>");
            }
        }

        sb.Append("<footer>").Append(E(titulo)).Append(" · EasyStok</footer>");
        sb.Append("</body></html>");
        return sb.ToString();
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

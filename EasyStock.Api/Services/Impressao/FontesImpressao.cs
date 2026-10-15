using Microsoft.AspNetCore.StaticFiles;

namespace EasyStock.Api.Services.Impressao;

/// <summary>
/// Fontes do impresso (S49) servidas pelo <c>UseStaticFiles</c>, que roda antes do <c>UseCors</c>. O console
/// exibe o impresso em outra origem, e <c>@font-face</c> cross-origin exige CORS. As fontes são públicas (OFL) e
/// não levam credencial, então a resposta libera qualquer origem; o resto do wwwroot não muda.
/// </summary>
public static class FontesImpressao
{
    public static void LiberarCors(StaticFileResponseContext ctx)
    {
        if (ctx.Context.Request.Path.StartsWithSegments(ImpressoHtml.CaminhoFontes, StringComparison.OrdinalIgnoreCase))
            ctx.Context.Response.Headers.AccessControlAllowOrigin = "*";
    }
}

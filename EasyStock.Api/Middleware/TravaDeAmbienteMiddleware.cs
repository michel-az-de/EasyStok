namespace EasyStock.Api.Middleware;

/// <summary>
/// Trava contra massa de teste na produção (issue #3251, P2 da #3114).
///
/// <para>
/// O APK de homologação manda <c>X-Cdb-Ambiente: homologacao</c> em todo pedido (nativo e web). Esta
/// Api recusa, antes de PWA, login, pareamento e sync, quem se declara qualquer coisa que não
/// <c>oficial</c>. Quem não manda o cabeçalho (APK oficial, Console, Web, PWA) segue como antes.
/// </para>
///
/// <para>
/// É defesa em profundidade: o APK de homologação já não tem origem da produção (#3114). A trava
/// pega o caso em que a produção é alcançada por outro nome. Fica ligada por padrão, porque a imagem
/// sobe como Production; só o desenvolvimento e o laboratório a desligam com
/// <c>Ambiente:AceitaClienteDeHomologacao=true</c>.
/// </para>
/// </summary>
public sealed class TravaDeAmbienteMiddleware(RequestDelegate next, IConfiguration configuracao, ILogger<TravaDeAmbienteMiddleware> logger)
{
    public const string Cabecalho = "X-Cdb-Ambiente";
    public const string ChaveAceitaHomologacao = "Ambiente:AceitaClienteDeHomologacao";
    private const string Oficial = "oficial";

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue(Cabecalho, out var valores)
            || (valores.Count == 1 && string.Equals(valores[0], Oficial, StringComparison.Ordinal))
            || AceitaHomologacao())
        {
            await next(context);
            return;
        }

        logger.LogWarning(
            "Trava de ambiente: {Method} {Path} recusado. {Cabecalho}={Valor}",
            Sanitizar(context.Request.Method), Sanitizar(context.Request.Path.Value), Cabecalho, Sanitizar(valores.ToString()));

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Title = "Ambiente recusado",
            Detail = "Este aplicativo é de homologação e não grava na produção. Use o APK oficial.",
            Extensions = { ["codigo"] = "AMBIENTE_RECUSADO" },
        }, options: null, contentType: "application/problem+json");
    }

    private bool AceitaHomologacao() =>
        bool.TryParse(configuracao[ChaveAceitaHomologacao], out var aceita) && aceita;

    private static string Sanitizar(string? valor)
    {
        if (string.IsNullOrEmpty(valor)) return string.Empty;
        var limpo = new string(valor.Where(c => !char.IsControl(c)).ToArray());
        return limpo.Length > 80 ? limpo[..80] : limpo;
    }
}

namespace EasyStock.Web.Constants;

/// <summary>
/// Cabeçalhos HTTP customizados usados pelo front-end para sinalizar
/// requisições fetch (AJAX) e permitir ao controller responder com JSON
/// em vez de redirect/view.
/// </summary>
public static class CustomHeaders
{
    /// <summary>
    /// Cabeçalho <c>X-Fetch</c> enviado com valor <c>"1"</c> quando o
    /// cliente espera resposta JSON em vez de render Razor.
    /// </summary>
    public const string FetchRequest = "X-Fetch";

    /// <summary>Valor esperado para <see cref="FetchRequest"/>.</summary>
    public const string FetchRequestEnabled = "1";
}

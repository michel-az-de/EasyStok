namespace EasyStock.Web.Services;

/// <summary>
/// Link relativo devolvido pela API (ex.: <c>/api/reports/artefatos/{token}</c>, #2668) aberto pelo navegador: no Web ele
/// resolveria contra o host do Web, que não serve <c>/api</c>. Prefixa com <c>PublicApiUrl</c>, como o cardápio impresso.
/// </summary>
public static class LinkDaApi
{
    public static string? Absoluto(string? publicApiUrl, string? link)
    {
        if (string.IsNullOrEmpty(link) || Uri.IsWellFormedUriString(link, UriKind.Absolute)) return link;
        var baseApi = (publicApiUrl ?? string.Empty).TrimEnd('/');
        return baseApi.Length == 0 ? link : baseApi + "/" + link.TrimStart('/');
    }
}

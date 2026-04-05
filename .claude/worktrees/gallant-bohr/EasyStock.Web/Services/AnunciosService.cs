namespace EasyStock.Web.Services;

public class AnunciosService(ApiClient api)
{
    public async Task<(bool Success, Stream? Stream, string? Error)> GerarStreamAsync(
        string produtoId, string canal, string tom, string foco, string? varId, string? contexto)
    {
        var qs = $"anuncios/gerar?produtoId={Uri.EscapeDataString(produtoId)}" +
                 $"&canal={Uri.EscapeDataString(canal)}" +
                 $"&tom={Uri.EscapeDataString(tom)}" +
                 $"&foco={Uri.EscapeDataString(foco)}";
        if (!string.IsNullOrEmpty(varId)) qs += $"&varId={Uri.EscapeDataString(varId)}";
        if (!string.IsNullOrEmpty(contexto)) qs += $"&contexto={Uri.EscapeDataString(contexto)}";

        var result = await api.GetStreamAsync(qs);
        return result.Success
            ? (true, result.Data, null)
            : (false, null, result.ErrorMessage ?? "Erro ao gerar anúncio.");
    }
}

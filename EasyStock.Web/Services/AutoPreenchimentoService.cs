namespace EasyStock.Web.Services;

/// <summary>
/// Proxy do auto-preenchimento de produto via IA (api/ia/completar-produto).
/// Sobreviveu a poda P03 (#1116), que removeu os anuncios com IA.
/// </summary>
public class AutoPreenchimentoService(ApiClient api)
{
    public async Task<(bool Success, Stream? Stream, string? Error)> CompletarProdutoStreamAsync(
        string nomeProduto, string? categoria, string? marca, string? instrucoes)
    {
        var body = new
        {
            nomeProduto,
            categoria,
            marca,
            instrucoes
        };

        var result = await api.PostStreamAsync("ia/completar-produto", body);
        return result.Success
            ? (true, result.Data, null)
            : (false, null, result.ErrorMessage ?? "Erro ao completar produto.");
    }
}

namespace EasyStock.Web.Services;

public class AnunciosService(ApiClient api, SessionService session)
{
    public async Task<(bool Success, Stream? Stream, string? Error)> GerarStreamAsync(
        string produtoId, string canal, string tom, string foco, string? varId, string? contexto)
    {
        // Compose InstrucoesComplementares from canal, tom, foco, contexto
        var instrucoes = $"Canal: {canal}. Tom: {tom}. Foco: {foco}.";
        if (!string.IsNullOrEmpty(contexto))
            instrucoes += $" {contexto}";

        _ = Guid.TryParse(session.GetEmpresaId(), out var empresaId);
        _ = Guid.TryParse(produtoId, out var prodId);
        Guid? variId = Guid.TryParse(varId, out var vid) ? vid : null;

        var body = new
        {
            empresaId,
            produtoId = prodId,
            produtoVariacaoId = variId,
            instrucoesComplementares = instrucoes
        };

        // API expects POST to ia/anuncio (not GET to anuncios/gerar)
        var result = await api.PostStreamAsync("ia/anuncio", body);
        return result.Success
            ? (true, result.Data, null)
            : (false, null, result.ErrorMessage ?? "Erro ao gerar anúncio.");
    }
}

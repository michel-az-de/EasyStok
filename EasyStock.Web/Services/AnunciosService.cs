using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Services;

public class AnunciosService(ApiClient api, SessionService session)
{
    private Guid GetEmpresaId() =>
        Guid.TryParse(session.GetEmpresaId(), out var id) ? id : Guid.Empty;

    public Task<ApiResult<List<AnuncioIaApi>>> ListarSalvosAsync(string produtoId) =>
        api.GetAsync<List<AnuncioIaApi>>($"ia/anuncios/{produtoId}?empresaId={GetEmpresaId()}");

    public Task<ApiResult<object>> SalvarAnuncioAsync(string produtoId, string? variacaoId, string titulo, string conteudo, string? instrucoes) =>
        api.PostAsync<object>("ia/anuncio/salvar", new
        {
            empresaId = GetEmpresaId(),
            produtoId = Guid.TryParse(produtoId, out var pid) ? pid : Guid.Empty,
            produtoVariacaoId = Guid.TryParse(variacaoId, out var vid) ? (Guid?)vid : null,
            titulo,
            conteudo,
            instrucoesUsadas = instrucoes,
            tokensConsumidos = 0
        });

    public Task<ApiResult<bool>> DeletarAnuncioAsync(string id) =>
        api.DeleteAsync($"ia/anuncios/{id}?empresaId={GetEmpresaId()}");

    public async Task<(bool Success, Stream? Stream, string? Error)> GerarStreamAsync(
        string produtoId, string canal, string tom, string foco, string? varId, string? contexto)
    {
        if (!Guid.TryParse(produtoId, out var prodGuid))
            return (false, null, "Produto inválido. Selecione um produto da lista.");

        // Constrói instruções complementares a partir dos parâmetros de configuração da UI
        var instrucoes = new List<string>
        {
            $"Canal: {canal}",
            $"Tom: {tom}",
            $"Foco: {foco}"
        };
        if (!string.IsNullOrWhiteSpace(contexto))
            instrucoes.Add(contexto);

        var body = new
        {
            empresaId = GetEmpresaId(),
            produtoId = prodGuid,
            produtoVariacaoId = Guid.TryParse(varId, out var varGuid) ? (Guid?)varGuid : null,
            instrucoesComplementares = string.Join(". ", instrucoes)
        };

        var result = await api.PostStreamAsync("ia/anuncio", body);
        return result.Success
            ? (true, result.Data, null)
            : (false, null, result.ErrorMessage ?? "Erro ao gerar anúncio.");
    }

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

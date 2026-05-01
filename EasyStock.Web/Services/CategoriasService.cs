using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Services;

public class CategoriasService(ApiClient api, SessionService session)
{
    private Guid GetEmpresaId() =>
        Guid.TryParse(session.GetEmpresaId(), out var id) ? id : Guid.Empty;

    public Task<ApiResult<List<CategoriaApi>>> ListarAsync() =>
        api.GetAsync<List<CategoriaApi>>($"categorias?empresaId={GetEmpresaId()}");

    public Task<ApiResult<object>> CriarAsync(string nome) =>
        api.PostAsync<object>("categorias", new
        {
            empresaId = GetEmpresaId(),
            nome,
            descricao = (string?)null,
            categoriaPaiId = (Guid?)null
        });

    public Task<ApiResult<object>> EditarAsync(string id, string nome) =>
        Guid.TryParse(id, out var catId)
            ? api.PutAsync<object>($"categorias/{id}", new
            {
                id = catId,
                empresaId = GetEmpresaId(),
                nome,
                descricao = (string?)null,
                categoriaPaiId = (Guid?)null
            })
            : Task.FromResult(ApiResult<object>.Fail("INVALID_ID", "Id de categoria inválido."));

    public Task<ApiResult<bool>> ExcluirAsync(string id) =>
        api.DeleteAsync($"categorias/{id}?empresaId={GetEmpresaId()}");

    public Task<ApiResult<object>> AtualizarLimiarAsync(string id, int? quantidadeMinima, int? quantidadeCritica) =>
        api.PatchAsync<object>($"categorias/{id}/limiar?empresaId={GetEmpresaId()}", new
        {
            quantidadeMinima,
            quantidadeCritica
        });
}

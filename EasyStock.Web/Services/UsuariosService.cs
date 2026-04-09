using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Services;

public class UsuariosService(ApiClient api)
{
    public Task<ApiResult<List<Usuario>>> ListarAsync() =>
        api.GetAsync<List<Usuario>>("usuarios");

    public Task<ApiResult<object>> ConvidarAsync(string nome, string email, string role, List<string> lojaIds) =>
        api.PostAsync<object>("usuarios/convite", new { nome, email, role, lojaIds });

    public Task<ApiResult<object>> EditarAsync(string id, object body) =>
        api.PatchAsync<object>($"usuarios/{id}", body);

    public Task<ApiResult<bool>> RemoverAsync(string id) =>
        api.DeleteAsync($"usuarios/{id}");
}

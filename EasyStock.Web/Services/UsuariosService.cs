using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Services;

public class UsuariosService(ApiClient api)
{
    public Task<ApiResult<List<Usuario>>> ListarAsync() =>
        api.GetAsync<List<Usuario>>("usuarios");

    public Task<ApiResult<object>> CriarAsync(string empresaId, string nome, string email, string senha) =>
        api.PostAsync<object>("usuarios", new
        {
            empresaId = Guid.TryParse(empresaId, out var eid) ? eid : Guid.Empty,
            nome,
            email,
            senha,
            perfilId = (Guid?)null,
            lojaId = (Guid?)null
        });

    public Task<ApiResult<object>> EditarAsync(string id, string nome) =>
        Guid.TryParse(id, out var uid)
            ? api.PutAsync<object>($"usuarios/{id}", new { usuarioId = uid, nome, email = (string?)null })
            : Task.FromResult(ApiResult<object>.Fail("INVALID_ID", "Id de usuário inválido."));

    public Task<ApiResult<bool>> RemoverAsync(string id) =>
        api.DeleteAsync($"usuarios/{id}");
}

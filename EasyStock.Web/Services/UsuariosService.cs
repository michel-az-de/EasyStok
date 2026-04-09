using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Services;

public class UsuariosService(ApiClient api, SessionService session)
{
    private Guid GetEmpresaId() =>
        Guid.TryParse(session.GetEmpresaId(), out var id) ? id : Guid.Empty;

    public Task<ApiResult<List<Usuario>>> ListarAsync() =>
        api.GetAsync<List<Usuario>>($"usuarios?empresaId={GetEmpresaId()}");

    public Task<ApiResult<object>> CriarAsync(string empresaId, string nome, string email, string senha) =>
        Guid.TryParse(empresaId, out var eid) && eid != Guid.Empty
            ? api.PostAsync<object>("usuarios", new
            {
                empresaId = eid,
                nome,
                email,
                senha,
                perfilId = (Guid?)null,
                lojaId = (Guid?)null
            })
            : Task.FromResult(ApiResult<object>.Fail("EMPRESA_NAO_IDENTIFICADA", "Não foi possível identificar a empresa do usuário atual."));

    public Task<ApiResult<object>> EditarAsync(string id, string nome) =>
        Guid.TryParse(id, out var uid)
            ? api.PutAsync<object>($"usuarios/{id}", new { usuarioId = uid, nome, email = (string?)null })
            : Task.FromResult(ApiResult<object>.Fail("INVALID_ID", "Id de usuário inválido."));

    public Task<ApiResult<bool>> RemoverAsync(string id) =>
        api.DeleteAsync($"usuarios/{id}?empresaId={GetEmpresaId()}");
}

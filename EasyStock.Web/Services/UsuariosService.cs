using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Services;

public class UsuariosService(ApiClient api, SessionService session) : TenantServiceBase(session)
{

    public Task<ApiResult<List<Usuario>>> ListarAsync() =>
        api.GetAsync<List<Usuario>>($"usuarios?empresaId={GetEmpresaId()}&page=1&pageSize=200");

    /// <summary>
    /// N9: cria o convidado, sem senha. A API manda o convite com link por e-mail (e por WhatsApp quando há telefone e o
    /// atestado da dona). A senha nunca passa por aqui: quem a define é a própria pessoa, no aceite do convite.
    /// </summary>
    public Task<ApiResult<object>> CriarAsync(
        string empresaId, string nome, string email, Guid? perfilId = null, Guid? lojaId = null,
        string? telefone = null, bool atestaOptInWhatsApp = false) =>
        Guid.TryParse(empresaId, out var eid) && eid != Guid.Empty
            ? api.PostAsync<object>("usuarios", new
            {
                empresaId = eid,
                nome,
                email,
                perfilId,
                lojaId,
                telefone = string.IsNullOrWhiteSpace(telefone) ? null : telefone.Trim(),
                atestaOptInWhatsApp
            })
            : Task.FromResult(ApiResult<object>.Fail("EMPRESA_NAO_IDENTIFICADA", "Não foi possível identificar a empresa do usuário atual."));

    public Task<ApiResult<object>> ReenviarConviteAsync(string id) =>
        Guid.TryParse(id, out _)
            ? api.PostAsync<object>($"usuarios/{id}/convite", new { })
            : Task.FromResult(ApiResult<object>.Fail("INVALID_ID", "Id de usuário inválido."));

    public Task<ApiResult<object>> EditarAsync(string id, string nome) =>
        Guid.TryParse(id, out var uid)
            ? api.PutAsync<object>($"usuarios/{id}", new { empresaId = GetEmpresaId(), usuarioId = uid, nome, email = (string?)null })
            : Task.FromResult(ApiResult<object>.Fail("INVALID_ID", "Id de usuário inválido."));

    public Task<ApiResult<bool>> RemoverAsync(string id) =>
        api.DeleteAsync($"usuarios/{id}?empresaId={GetEmpresaId()}");

    public Task<ApiResult<object>> AtribuirPerfilAsync(string userId, Guid perfilId, Guid? lojaId) =>
        Guid.TryParse(userId, out _)
            ? api.PutAsync<object>($"usuarios/{userId}/perfis", new
            {
                empresaId = GetEmpresaId(),
                perfilId,
                lojaId
            })
            : Task.FromResult(ApiResult<object>.Fail("INVALID_ID", "Id de usuário inválido."));
}

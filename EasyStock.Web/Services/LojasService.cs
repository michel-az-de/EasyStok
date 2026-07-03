using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Services;

public class LojasService(ApiClient api, SessionService session) : TenantServiceBase(session)
{

    public Task<ApiResult<List<Loja>>> ListarAsync() =>
        api.GetAsync<List<Loja>>($"lojas?empresaId={GetEmpresaId()}");

    public Task<ApiResult<object>> CriarAsync(string nome, string? cidade = null, string? telefone = null, string? descricao = null) =>
        api.PostAsync<object>("lojas", new
        {
            empresaId = GetEmpresaId(),
            nome,
            descricao,
            documento = (string?)null,
            endereco = cidade,
            telefone
        });

    public Task<ApiResult<object>> EditarAsync(string id, string nome) =>
        Guid.TryParse(id, out var lojaId)
            ? api.PutAsync<object>($"lojas/{id}", new
            {
                lojaId,
                empresaId = GetEmpresaId(),
                nome,
                descricao = (string?)null,
                documento = (string?)null,
                endereco = (string?)null,
                telefone = (string?)null
            })
            : Task.FromResult(ApiResult<object>.Fail("INVALID_ID", "Id de loja inválido."));

    public Task<ApiResult<bool>> ExcluirAsync(string id) =>
        api.DeleteAsync($"lojas/{id}?empresaId={GetEmpresaId()}");

    public Task<ApiResult<object>> ReativarAsync(string id) =>
        api.PostAsync<object>($"lojas/{id}/reativar?empresaId={GetEmpresaId()}", new { });
}

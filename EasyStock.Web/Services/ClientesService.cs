using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Services;

/// <summary>
/// Onda P1 — UI Web do módulo Cliente. Espelha
/// <see cref="FornecedoresService"/> usando endpoints de
/// <c>/api/clientes</c> e <c>/api/mobile/clients</c>.
/// </summary>
public class ClientesService(ApiClient api, SessionService session)
{
    private Guid GetEmpresaId() =>
        Guid.TryParse(session.GetEmpresaId(), out var id) ? id : Guid.Empty;

    private static ApiResult<T> EmpresaErr<T>() =>
        ApiResult<T>.Fail("EMPRESA_INVALIDA", "Loja não identificada. Selecione uma loja e tente novamente.");

    // ── CRUD raiz ──────────────────────────────────────────────────────

    public Task<ApiResult<List<Cliente>>> ListarAsync(string? status = null, string? search = null)
    {
        var qs = $"clientes?empresaId={GetEmpresaId()}&page=1&pageSize=200";
        if (status == "ativo") qs += "&ativo=true";
        else if (status == "inativo") qs += "&ativo=false";
        if (!string.IsNullOrEmpty(search)) qs += $"&search={Uri.EscapeDataString(search)}";
        return api.GetAsync<List<Cliente>>(qs);
    }

    public Task<ApiResult<ClienteDetalhe>> ObterAsync(string id) =>
        api.GetAsync<ClienteDetalhe>($"clientes/{id}?empresaId={GetEmpresaId()}");

    public Task<ApiResult<Cliente>> CriarAsync(
        string nome, string? apt, string? endereco, string? telefone,
        string? email, string? documento, string? observacoes)
    {
        var empresaId = GetEmpresaId();
        if (empresaId == Guid.Empty) return Task.FromResult(EmpresaErr<Cliente>());

        return api.PostAsync<Cliente>("clientes", new
        {
            empresaId,
            nome,
            apt,
            endereco,
            telefone,
            email,
            documento,
            observacoes
        });
    }

    public Task<ApiResult<Cliente>> EditarAsync(string id,
        string nome, string? apt, string? endereco, string? telefone,
        string? email, string? documento, string? observacoes)
    {
        if (!Guid.TryParse(id, out var cid))
            return Task.FromResult(ApiResult<Cliente>.Fail("INVALID_ID", "ID de cliente inválido."));

        var empresaId = GetEmpresaId();
        if (empresaId == Guid.Empty) return Task.FromResult(EmpresaErr<Cliente>());

        return api.PatchAsync<Cliente>($"clientes/{id}", new
        {
            id = cid,
            empresaId,
            nome,
            apt,
            endereco,
            telefone,
            email,
            documento,
            observacoes,
            origem = "web"
        });
    }

    public Task<ApiResult<bool>> ExcluirAsync(string id) =>
        api.DeleteAsync($"clientes/{id}?empresaId={GetEmpresaId()}");

    public Task<ApiResult<object>> ReativarAsync(string id) =>
        api.PostAsync<object>($"clientes/{id}/reativar?empresaId={GetEmpresaId()}", new { });

    // ── Sub-recursos ──────────────────────────────────────────────────

    public Task<ApiResult<object>> AddEnderecoAsync(string clienteId,
        string? tipo, string? logradouro, string? numero, string? complemento,
        string? bairro, string? cidade, string? estado, string? cep, string? pais,
        string? referencia, bool padrao)
    {
        var empresaId = GetEmpresaId();
        if (empresaId == Guid.Empty) return Task.FromResult(EmpresaErr<object>());

        return api.PostAsync<object>($"clientes/{clienteId}/enderecos", new
        {
            empresaId,
            clienteId = Guid.Parse(clienteId),
            tipo,
            logradouro,
            numero,
            complemento,
            bairro,
            cidade,
            estado,
            cep,
            pais,
            referencia,
            padrao
        });
    }

    public Task<ApiResult<bool>> RemoveEnderecoAsync(string clienteId, string enderecoId) =>
        api.DeleteAsync($"clientes/{clienteId}/enderecos/{enderecoId}?empresaId={GetEmpresaId()}");

    public Task<ApiResult<object>> AddTelefoneAsync(string clienteId,
        string numero, string? tipo, bool whatsapp, bool principal, string? observacao)
    {
        var empresaId = GetEmpresaId();
        if (empresaId == Guid.Empty) return Task.FromResult(EmpresaErr<object>());

        return api.PostAsync<object>($"clientes/{clienteId}/telefones", new
        {
            empresaId,
            clienteId = Guid.Parse(clienteId),
            numero,
            tipo,
            whatsapp,
            principal,
            observacao
        });
    }

    public Task<ApiResult<bool>> RemoveTelefoneAsync(string clienteId, string telefoneId) =>
        api.DeleteAsync($"clientes/{clienteId}/telefones/{telefoneId}?empresaId={GetEmpresaId()}");

    public Task<ApiResult<object>> AddDocumentoAsync(string clienteId,
        string tipo, string valor, string? emissor, DateTime? emitidoEm, DateTime? validoAte, bool principal)
    {
        var empresaId = GetEmpresaId();
        if (empresaId == Guid.Empty) return Task.FromResult(EmpresaErr<object>());

        return api.PostAsync<object>($"clientes/{clienteId}/documentos", new
        {
            empresaId,
            clienteId = Guid.Parse(clienteId),
            tipo,
            valor,
            emissor,
            emitidoEm,
            validoAte,
            principal
        });
    }

    public Task<ApiResult<bool>> RemoveDocumentoAsync(string clienteId, string documentoId) =>
        api.DeleteAsync($"clientes/{clienteId}/documentos/{documentoId}?empresaId={GetEmpresaId()}");

    // ── Mobile (clientes criados no app) ──────────────────────────────

    public Task<ApiResult<List<MobileClienteSummary>>> ListarMobileAsync(bool pendingOnly = false)
    {
        var qs = $"mobile/clients?empresaId={GetEmpresaId()}";
        if (pendingOnly) qs += "&pendingOnly=true";
        return api.GetAsync<List<MobileClienteSummary>>(qs);
    }

    /// <summary>Linka a Cliente ERP existente (passa erpClienteId) ou promove (passa null).</summary>
    public Task<ApiResult<object>> LinkMobileAsync(string mobileClientId, Guid? erpClienteId) =>
        api.PostAsync<object>($"mobile/clients/{mobileClientId}/link", new { erpClienteId });

    public Task<ApiResult<object>> UnlinkMobileAsync(string mobileClientId) =>
        api.PostAsync<object>($"mobile/clients/{mobileClientId}/unlink", new { });
}

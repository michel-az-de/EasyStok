using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Services;

/// <summary>
/// Base dos services BFF por-tenant (issue 810). Consolida o que ~24 services copiavam
/// identicamente: <c>GetEmpresaId()</c> (empresaId da sessao, derivado do JWT) e as
/// fabricas de erro <c>EmpresaErr</c>/<c>IdErr</c>. O <c>ApiClient</c> continua injetado
/// em cada service porque o uso varia; aqui fica so o que era boilerplate puro.
/// </summary>
public abstract class TenantServiceBase(SessionService session)
{
    // Exposta pros derivados: capturar o MESMO parametro no derivado e na base
    // e CS9107 (erro no CI via -warnaserror, issue 821).
    protected SessionService Session { get; } = session;

    protected Guid GetEmpresaId() =>
        Guid.TryParse(Session.GetEmpresaId(), out var id) ? id : Guid.Empty;

    protected static ApiResult<T> EmpresaErr<T>() =>
        ApiResult<T>.Fail("EMPRESA_INVALIDA", "Loja não identificada. Selecione uma loja e tente novamente.");

    // Issue 808: id malformado na rota vira 400 em vez de FormatException/500.
    protected static ApiResult<T> IdErr<T>() =>
        ApiResult<T>.Fail("ID_INVALIDO", "Identificador inválido.", 400);
}

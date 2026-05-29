namespace EasyStock.Api.Http;

[ApiController]
public abstract class EasyStockControllerBase : ControllerBase
{
    // ── Success helpers ─────────────────────────────────────────────────────

    /// <summary>200 with { data, meta: {} }</summary>
    protected IActionResult DataOk<T>(T data) =>
        base.Ok(new ApiResponse<T>(data, new { }));

    /// <summary>200 with { data: [...], meta: { total, pages, page, limit } }</summary>
    protected IActionResult DataPaged<T>(IEnumerable<T> items, int total, int page, int limit)
    {
        var pages = limit > 0 ? (int)Math.Ceiling((double)total / limit) : 0;
        return base.Ok(new ApiResponse<IEnumerable<T>>(items, new PagedMeta(total, pages, page, limit)));
    }

    /// <summary>201 with { data, meta: {} }</summary>
    protected IActionResult DataCreated<T>(string uri, T data) =>
        base.Created(uri, new ApiResponse<T>(data, new { }));

    // ── Error helpers ────────────────────────────────────────────────────────

    /// <summary>400 with { error: { code, message, detail } }</summary>
    protected IActionResult DataBadRequest(string message, string? detail = null) =>
        base.BadRequest(new ApiErrorResponse(new ApiError("BAD_REQUEST", message, detail, null)));

    /// <summary>404 with { error: { code: NOT_FOUND, ... } }</summary>
    protected IActionResult DataNotFound(string message = "Recurso não encontrado.", string? detail = null) =>
        base.NotFound(new ApiErrorResponse(new ApiError("NOT_FOUND", message, detail, null)));

    protected bool TryResolveEmpresaId(
        ICurrentUserAccessor currentUser,
        Guid? requestedEmpresaId,
        out Guid empresaId,
        out IActionResult? error)
    {
        error = null;
        empresaId = requestedEmpresaId.GetValueOrDefault();

        if (currentUser.Nivel == NivelAcesso.SuperAdmin)
        {
            if (empresaId == Guid.Empty)
            {
                error = DataBadRequest("EmpresaId é obrigatório.");
                return false;
            }

            return true;
        }

        if (currentUser.EmpresaId != Guid.Empty)
        {
            if (requestedEmpresaId.HasValue && requestedEmpresaId.Value != Guid.Empty && requestedEmpresaId.Value != currentUser.EmpresaId)
            {
                error = Forbid();
                empresaId = Guid.Empty;
                return false;
            }

            empresaId = currentUser.EmpresaId;
            return true;
        }

        if (empresaId == Guid.Empty)
        {
            error = DataBadRequest("EmpresaId é obrigatório.");
            return false;
        }

        return true;
    }

    // ── Sorting helpers ──────────────────────────────────────────────────────

    /// <summary>Validates and normalises the order parameter to "asc" or "desc".</summary>
    protected static string NormaliseOrder(string? order) =>
        string.Equals(order, "desc", StringComparison.OrdinalIgnoreCase) ? "desc" : "asc";

    // ── Pagination helpers ───────────────────────────────────────────────────

    /// <summary>Clamps page (min 1) and pageSize (1..maxPageSize) to safe values.</summary>
    protected static (int Page, int PageSize) NormalisePage(int page, int pageSize, int maxPageSize = 200) =>
        (Math.Max(1, page), Math.Clamp(pageSize, 1, maxPageSize));
}

namespace EasyStock.Api.Controllers;

/// <summary>
/// F10-D — Consulta de auditoria universal (entity_alteracoes).
/// Permite buscar timeline de alteracoes por entidade, tipo ou empresa.
/// Autenticacao: JWT (web/api). SuperAdmin pode consultar cross-tenant.
/// </summary>
[ApiController]
[Route("api/audit")]
[Authorize]
public class EntityAuditController(
    IEntityAuditQueries entityAudit,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    /// <summary>
    /// Timeline de uma entidade especifica.
    /// GET /api/audit/entity/{tipoEntidade}/{entidadeId}?page=1&amp;pageSize=50
    /// </summary>
    [HttpGet("entity/{tipoEntidade}/{entidadeId:guid}")]
    public async Task<IActionResult> GetByEntity(
        string tipoEntidade,
        Guid entidadeId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var empresaId = currentUser.EmpresaId;
        if (empresaId == Guid.Empty)
            return Forbid();

        pageSize = Math.Clamp(pageSize, 1, 100);
        page = Math.Max(1, page);

        var (items, total) = await entityAudit.PorEntidadeAsync(empresaId, tipoEntidade, entidadeId, page, pageSize, ct);

        return DataOk(new { total, page, pageSize, items });
    }

    /// <summary>
    /// Timeline cross-entidade para um cliente (pedidos, vendas, pagamentos, etc).
    /// GET /api/audit/client-timeline/{clienteId}?page=1&amp;pageSize=50
    /// </summary>
    [HttpGet("client-timeline/{clienteId:guid}")]
    public async Task<IActionResult> GetClientTimeline(
        Guid clienteId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var empresaId = currentUser.EmpresaId;
        if (empresaId == Guid.Empty)
            return Forbid();

        pageSize = Math.Clamp(pageSize, 1, 100);
        page = Math.Max(1, page);

        var (items, total) = await entityAudit.TimelineClienteAsync(empresaId, clienteId, page, pageSize, ct);

        return DataOk(new { total, page, pageSize, items });
    }

    /// <summary>
    /// Resumo de auditoria da empresa — contagem por tipo de entidade.
    /// GET /api/audit/summary
    /// </summary>
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(CancellationToken ct = default)
    {
        var empresaId = currentUser.EmpresaId;
        if (empresaId == Guid.Empty)
            return Forbid();

        var summary = await entityAudit.ResumoPorTipoAsync(empresaId, ct);
        var total = summary.Sum(s => s.Count);

        return DataOk(new { total, byType = summary });
    }
}

namespace EasyStock.Api.Controllers;

[ApiController]
[Route("api/admin/audit-admin")]
[Authorize(Policy = "SuperAdmin")]
public class AdminAuditController(IAdminAuditLogQueries auditLogs) : EasyStockControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetLogs(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? acao = null,
        [FromQuery] DateTime? de = null,
        [FromQuery] DateTime? ate = null,
        CancellationToken ct = default)
    {
        (page, pageSize) = NormalisePage(page, pageSize, maxPageSize: 100);

        var (logs, total) = await auditLogs.ListarPorAcaoExataAsync(acao, de, ate, page, pageSize, ct);

        return DataPaged(logs, total, page, pageSize);
    }
}

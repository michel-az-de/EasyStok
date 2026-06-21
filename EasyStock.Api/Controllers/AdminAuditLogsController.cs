using EasyStock.Api.Utilities;
using EasyStock.Application.Common;

namespace EasyStock.Api.Controllers;

[ApiController]
[Route("api/admin/audit-logs")]
[Authorize(Policy = "SuperAdmin")]
public class AdminAuditLogsController(IAdminAuditLogQueries auditLogs) : EasyStockControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetLogs(
        [FromQuery] Guid? tenantId,
        [FromQuery] string? acao,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? export,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        (page, pageSize) = NormalisePage(page, pageSize);

        var filtro = new AdminAuditLogFiltro(tenantId, acao, from, to, page, pageSize);

        // CSV export
        if (string.Equals(export, "csv", StringComparison.OrdinalIgnoreCase))
        {
            var all = await auditLogs.ExportarAsync(filtro, ct);

            var headers = new[] { "AdminEmail", "Acao", "TenantId", "Detalhes", "IP", "CriadoEm" };
            var rows = all.Select(r => new string[]
            {
                PiiMaskingHelper.MaskEmail(r.AdminEmail) ?? "",
                r.Acao ?? "",
                r.TenantId.ToString() ?? "",
                r.Detalhes ?? "",
                PiiMaskingHelper.MaskIpAddress(r.Ip) ?? "",
                r.CriadoEm.ToString("O")
            });

            // CSV central (#612): BOM UTF-8, separador ';', anti-injecao de formula + quoting RFC-4180.
            return File(Csv.Build(headers, rows), "text/csv", "admin-audit-logs.csv");
        }

        var (items, total) = await auditLogs.ListarAsync(filtro, ct);

        // Mask PII before returning
        var logs = items.Select(x => new
        {
            x.Id,
            AdminEmail = PiiMaskingHelper.MaskEmail(x.AdminEmail),
            x.Acao,
            x.TenantId,
            x.Detalhes,
            Ip = PiiMaskingHelper.MaskIpAddress(x.Ip),
            x.CriadoEm
        }).ToList();

        return DataPaged(logs, total, page, pageSize);
    }
}

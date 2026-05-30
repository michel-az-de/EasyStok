using EasyStock.Api.Utilities;
using System.Text;

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

            var sb = new StringBuilder();
            sb.AppendLine("AdminEmail,Acao,TenantId,Detalhes,IP,CriadoEm");
            foreach (var r in all)
            {
                var maskedEmail = PiiMaskingHelper.MaskEmail(r.AdminEmail);
                var maskedIp = PiiMaskingHelper.MaskIpAddress(r.Ip);
                sb.AppendLine($"\"{maskedEmail}\",\"{r.Acao}\",\"{r.TenantId}\",\"{r.Detalhes?.Replace("\"", "\"\"")}\",\"{maskedIp}\",\"{r.CriadoEm:O}\"");
            }

            return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", "admin-audit-logs.csv");
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

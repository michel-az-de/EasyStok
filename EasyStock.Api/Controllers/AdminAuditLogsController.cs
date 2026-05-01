using EasyStock.Api.Http;
using EasyStock.Api.Utilities;
using EasyStock.Infra.Postgre.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace EasyStock.Api.Controllers;

[ApiController]
[Route("api/admin/audit-logs")]
[Authorize(Policy = "SuperAdmin")]
public class AdminAuditLogsController(EasyStockDbContext db) : EasyStockControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetLogs(
        [FromQuery] Guid? tenantId,
        [FromQuery] string? acao,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? export,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        (page, pageSize) = NormalisePage(page, pageSize);

        var query = db.AdminAuditLogs.AsQueryable();

        if (tenantId.HasValue)
            query = query.Where(x => x.TenantId == tenantId.Value);

        if (!string.IsNullOrWhiteSpace(acao))
            query = query.Where(x => x.Acao.Contains(acao));

        if (from.HasValue)
            query = query.Where(x => x.CriadoEm >= from.Value.ToUniversalTime());

        if (to.HasValue)
            query = query.Where(x => x.CriadoEm <= to.Value.ToUniversalTime().AddDays(1));

        query = query.OrderByDescending(x => x.CriadoEm);

        // CSV export
        if (string.Equals(export, "csv", StringComparison.OrdinalIgnoreCase))
        {
            var all = await query
                .Select(x => new { x.AdminEmail, x.Acao, x.TenantId, x.Detalhes, x.Ip, x.CriadoEm })
                .ToListAsync();

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

        var total = await query.CountAsync();
        var logRecords = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                x.Id,
                x.AdminEmail,
                x.Acao,
                x.TenantId,
                x.Detalhes,
                x.Ip,
                x.CriadoEm
            })
            .ToListAsync();

        // Mask PII before returning
        var logs = logRecords.Select(x => new
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

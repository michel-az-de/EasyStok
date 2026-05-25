using EasyStock.Api.Http;
using EasyStock.Infra.Postgre.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.Controllers;

[ApiController]
[Route("api/admin/audit-admin")]
[Authorize(Policy = "SuperAdmin")]
public class AdminAuditController(EasyStockDbContext db) : EasyStockControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetLogs(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? acao = null,
        [FromQuery] DateTime? de = null,
        [FromQuery] DateTime? ate = null)
    {
        (page, pageSize) = NormalisePage(page, pageSize, maxPageSize: 100);

        var query = db.AdminAuditLogs.AsQueryable();

        if (!string.IsNullOrWhiteSpace(acao))
            query = query.Where(l => l.Acao == acao);

        if (de.HasValue)
            query = query.Where(l => l.CriadoEm >= de.Value.ToUniversalTime());

        if (ate.HasValue)
            query = query.Where(l => l.CriadoEm < ate.Value.ToUniversalTime().AddDays(1));

        var total = await query.CountAsync();

        var logs = await query
            .OrderByDescending(l => l.CriadoEm)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new
            {
                l.Id,
                l.AdminEmail,
                l.Acao,
                l.Detalhes,
                l.TenantId,
                l.Ip,
                l.CriadoEm
            })
            .ToListAsync();

        return DataPaged(logs, total, page, pageSize);
    }
}

using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using System.Security.Claims;

namespace EasyStock.Api.Services;

public class AdminAuditService(EasyStockDbContext db, IHttpContextAccessor http)
{
    public async Task LogAsync(string acao, string? detalhes = null, Guid? tenantId = null)
    {
        var email = http.HttpContext?.User?.FindFirstValue(ClaimTypes.Email)
                    ?? http.HttpContext?.User?.FindFirstValue("email")
                    ?? "system";
        var ip = http.HttpContext?.Connection.RemoteIpAddress?.ToString();
        db.AdminAuditLogs.Add(AdminAuditLog.Criar(email, acao, detalhes, tenantId, ip));
        await db.CommitAsync();
    }
}

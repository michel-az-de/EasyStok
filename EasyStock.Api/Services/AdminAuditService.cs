using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using System.Security.Claims;

namespace EasyStock.Api.Services;

public class AdminAuditService(EasyStockDbContext db, IHttpContextAccessor http)
{
    public async Task LogAsync(string acao, string? detalhes = null, Guid? tenantId = null)
    {
        // "anonymous" quando ha HttpContext sem claim (chamada autenticada mas sem email no token);
        // "system" quando nao ha HttpContext (background job, scheduler) — distincao util na auditoria.
        var ctx = http.HttpContext;
        string email;
        if (ctx is null)
        {
            email = "system";
        }
        else
        {
            email = ctx.User?.FindFirstValue(ClaimTypes.Email)
                    ?? ctx.User?.FindFirstValue("email")
                    ?? "anonymous";
        }
        var ip = ctx?.Connection.RemoteIpAddress?.ToString();
        db.AdminAuditLogs.Add(AdminAuditLog.Criar(email, acao, detalhes, tenantId, ip));
        await db.CommitAsync();
    }
}

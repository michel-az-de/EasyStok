using EasyStock.Infra.Postgre.Data;
using System.Security.Claims;

namespace EasyStock.Api.Services;

public class AdminAuditService(EasyStockDbContext db, IHttpContextAccessor http, ILogger<AdminAuditService> logger)
{
    /// <summary>
    /// Log de ação do operador admin. Use <paramref name="motivo"/> para a justificativa
    /// digitada pelo operador (LGPD compliance) e <paramref name="entidadeAfetadaId"/> para
    /// permitir filtro "me mostre tudo que foi feito no usuário X" no dashboard.
    /// </summary>
    public async Task LogAsync(
        string acao,
        string? detalhes = null,
        Guid? tenantId = null,
        string? motivo = null,
        Guid? entidadeAfetadaId = null)
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
        // Honra X-Forwarded-For: atras do Caddy/Docker o RemoteIpAddress e a bridge interna
        // (::ffff:172.x), nao o cliente real. Mesmo padrao de CurrentUserAccessor.Ip: o 1o IP
        // da cadeia e o cliente original.
        var fwd = ctx?.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        var ip = !string.IsNullOrWhiteSpace(fwd)
            ? fwd.Split(',')[0].Trim()
            : ctx?.Connection.RemoteIpAddress?.ToString();
        db.AdminAuditLogs.Add(AdminAuditLog.Criar(email, acao, detalhes, tenantId, ip, motivo, entidadeAfetadaId));
        await db.CommitAsync();
    }

    /// <summary>
    /// Variante best-effort de <see cref="LogAsync"/> (ADR-0030): para auditoria de operação
    /// corriqueira (ex.: mutações de ticket) onde a operação JÁ foi commitada e uma falha de
    /// auditoria NÃO pode derrubar a resposta. NUNCA usar para auditoria sensível (revelação de
    /// PII/LGPD), que deve continuar em <see cref="LogAsync"/> (que lança).
    /// </summary>
    public async Task TryLogAsync(
        string acao,
        string? detalhes = null,
        Guid? tenantId = null,
        string? motivo = null,
        Guid? entidadeAfetadaId = null)
    {
        try
        {
            await LogAsync(acao, detalhes, tenantId, motivo, entidadeAfetadaId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha (best-effort) ao auditar acao {Acao} tenant {TenantId}", acao, tenantId);
        }
    }
}

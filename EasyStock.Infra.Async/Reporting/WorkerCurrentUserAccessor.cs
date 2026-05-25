using EasyStock.Application.Ports.Output;
using EasyStock.Application.Reporting;
using EasyStock.Domain.Enums;
using Microsoft.AspNetCore.Http;

namespace EasyStock.Infra.Async.Reporting;

/// <summary>
/// Implementação de <see cref="ICurrentUserAccessor"/> para o Worker.
/// Lê de <see cref="IReportExecutionScope"/> (AsyncLocal) quando não há HttpContext.
/// Fail-fast se chamado fora de um scope válido — ADR-R06.
/// </summary>
/// <remarks>
/// Registrada no Worker como override do <see cref="ICurrentUserAccessor"/> padrão.
/// O Api mantém a implementação HTTP (<c>CurrentUserAccessor</c>).
/// </remarks>
public sealed class WorkerCurrentUserAccessor(
    IHttpContextAccessor? httpContextAccessor,
    IReportExecutionScope executionScope) : ICurrentUserAccessor
{
    public bool IsAuthenticated => true; // Worker executa apenas jobs autenticados

    public Guid EmpresaId
    {
        get
        {
            // HttpContext tem prioridade (Worker admin web calls)
            var ctx = httpContextAccessor?.HttpContext;
            if (ctx?.User?.Identity?.IsAuthenticated == true)
            {
                var claim = ctx.User.FindFirst("empresaId")?.Value;
                if (Guid.TryParse(claim, out var id)) return id;
            }

            // AsyncLocal (contexto da run de relatório)
            if (!executionScope.IsSet)
                throw new InvalidOperationException(
                    "WorkerCurrentUserAccessor: nenhum contexto ativo. " +
                    "Chame IReportExecutionScope.Begin() antes de acessar o contexto.");

            return executionScope.EmpresaId ?? Guid.Empty;
        }
    }

    public Guid UsuarioId
    {
        get
        {
            var ctx = httpContextAccessor?.HttpContext;
            if (ctx?.User?.Identity?.IsAuthenticated == true)
            {
                var claim = ctx.User.FindFirst("sub")?.Value;
                if (Guid.TryParse(claim, out var id)) return id;
            }

            if (!executionScope.IsSet)
                throw new InvalidOperationException(
                    "WorkerCurrentUserAccessor: nenhum contexto ativo.");

            return executionScope.UsuarioSolicitanteId;
        }
    }

    public NivelAcesso Nivel
    {
        get
        {
            var ctx = httpContextAccessor?.HttpContext;
            if (ctx?.User?.Identity?.IsAuthenticated == true)
            {
                var claim = ctx.User.FindFirst("nivel")?.Value;
                if (Enum.TryParse<NivelAcesso>(claim, out var nivel)) return nivel;
            }

            // No contexto do Worker, AdminSaaS é SuperAdmin; Tenant é Admin
            if (!executionScope.IsSet)
                throw new InvalidOperationException(
                    "WorkerCurrentUserAccessor: nenhum contexto ativo.");

            return executionScope.Contexto == Domain.Reporting.ReportContexto.AdminSaaS
                ? NivelAcesso.SuperAdmin
                : NivelAcesso.Admin;
        }
    }

    public bool TemPermissao(Permissao permissao)
    {
        if (Nivel == NivelAcesso.SuperAdmin) return true;
        return permissao != Permissao.ConfigurarSla;
    }
}

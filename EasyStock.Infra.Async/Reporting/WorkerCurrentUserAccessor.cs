using EasyStock.Application.Ports.Output;
using EasyStock.Application.Reporting;
using EasyStock.Domain.Enums;
using Microsoft.AspNetCore.Http;

namespace EasyStock.Infra.Async.Reporting;

/// <summary>
/// Implementação de <see cref="ICurrentUserAccessor"/> para o Worker.
/// Precedência: (1) HttpContext autenticado; (2) <see cref="IReportExecutionScope"/>
/// (AsyncLocal, run de relatório tenant-scoped); (3) contexto de SISTEMA do Worker.
/// </summary>
/// <remarks>
/// Registrada no Worker como override do <see cref="ICurrentUserAccessor"/> padrão.
/// O Api mantém a implementação HTTP (<c>CurrentUserAccessor</c>).
///
/// <para>
/// <b>Contexto de sistema (sem HttpContext e sem report scope):</b> os background
/// services do Worker (outbox, SLA, agendamento, banners, saúde de endpoints,
/// contingência fiscal) rodam sem usuário e são cross-tenant por natureza. Aqui
/// devolvemos <c>EmpresaId=Guid.Empty</c> e <c>Nivel=SuperAdmin</c> em vez de lançar:
/// o filtro global multi-tenant do <c>EasyStockDbContext</c> é <c>IsSuperAdmin ||
/// EmpresaId == CurrentTenantId</c>, e ele avalia <c>CurrentTenantId</c> (client-side)
/// em TODA query tenant-scoped sem <c>IgnoreQueryFilters</c> — o que antes lançava
/// "nenhum contexto ativo" e derrubava cada service de banco por tick (a versão
/// fail-fast do ADR-R06 pressupunha que só o motor de relatórios tocava o DbContext
/// no Worker; deixou de valer quando os jobs de notificação/SLA/outbox passaram a
/// tocar). Com <c>Nivel=SuperAdmin</c> o filtro deixa de lançar. O acesso cross-tenant
/// REAL ao banco ainda exige bypass de RLS explícito (<c>UseRowLevelSecurityBypass</c>)
/// no job — sem bypass a policy RLS fail-closa (0 linhas), nunca vaza. Relatórios
/// seguem tenant-scoped pelo ramo <c>executionScope.IsSet</c> (empresa real da run).
/// </para>
/// </remarks>
public sealed class WorkerCurrentUserAccessor(
    IHttpContextAccessor?    httpContextAccessor,
    IReportExecutionScope    executionScope) : ICurrentUserAccessor
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

            // AsyncLocal (run de relatório tenant-scoped) — se setado, empresa da run.
            // Senão: contexto de sistema do Worker (ver <remarks>). Guid.Empty em vez de
            // lançar; o acesso real cross-tenant exige bypass de RLS explícito no job.
            return executionScope.IsSet
                ? executionScope.EmpresaId ?? Guid.Empty
                : Guid.Empty;
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

            // Contexto de sistema (sem run de relatório): sem usuário. Ver <remarks>.
            return executionScope.IsSet
                ? executionScope.UsuarioSolicitanteId
                : Guid.Empty;
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

            // Run de relatório: AdminSaaS é SuperAdmin; Tenant é Admin.
            // Contexto de sistema (sem run): SuperAdmin — faz o filtro global
            // multi-tenant do EasyStockDbContext (IsSuperAdmin || ...) curto-circuitar
            // em vez de avaliar CurrentTenantId e lançar. Ver <remarks>.
            if (!executionScope.IsSet)
                return NivelAcesso.SuperAdmin;

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

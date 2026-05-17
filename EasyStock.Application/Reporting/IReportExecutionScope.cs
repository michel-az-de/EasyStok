using EasyStock.Domain.Reporting;

namespace EasyStock.Application.Reporting;

/// <summary>
/// Contexto de execução de uma run de relatório.
/// Mantido via AsyncLocal no Worker para que o EF Core Query Filter
/// continue funcionando sem precisar de HttpContext.
/// </summary>
public interface IReportExecutionScope
{
    /// <summary>Tenant dono da run. Null em contexto AdminSaaS.</summary>
    Guid? EmpresaId { get; }

    /// <summary>Usuário que solicitou (ou ID do admin SaaS).</summary>
    Guid UsuarioSolicitanteId { get; }

    /// <summary>Contexto de execução (Tenant ou AdminSaaS).</summary>
    ReportContexto Contexto { get; }

    /// <summary>True se um contexto foi inicializado neste fluxo assíncrono.</summary>
    bool IsSet { get; }

    /// <summary>
    /// Inicializa o contexto para a run corrente.
    /// Retorna um IDisposable que limpa o estado ao ser descartado.
    /// </summary>
    IDisposable Begin(Guid? empresaId, Guid usuarioSolicitanteId, ReportContexto contexto);
}

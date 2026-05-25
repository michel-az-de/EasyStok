using EasyStock.Application.Reporting;
using EasyStock.Domain.Reporting;

namespace EasyStock.Infra.Async.Reporting;

/// <summary>
/// Implementação de <see cref="IReportExecutionScope"/> via AsyncLocal.
/// Permite que o EF Core Query Filter funcione dentro de jobs assíncronos
/// sem precisar de HttpContext — ADR-R06.
/// </summary>
public sealed class ReportExecutionContext : IReportExecutionScope
{
    private static readonly AsyncLocal<ReportExecutionFrame?> _frame = new();

    public Guid? EmpresaId => _frame.Value?.EmpresaId;
    public Guid UsuarioSolicitanteId => _frame.Value?.UsuarioSolicitanteId ?? throw new InvalidOperationException(NotSetMessage);
    public ReportContexto Contexto => _frame.Value?.Contexto ?? throw new InvalidOperationException(NotSetMessage);
    public bool IsSet => _frame.Value is not null;

    private const string NotSetMessage =
        "Nenhum contexto de execução de relatório ativo. " +
        "O Worker deve chamar IReportExecutionScope.Begin() antes de executar o handler.";

    /// <summary>
    /// Inicializa o contexto para a run corrente. Retorna disposable que limpa ao sair.
    /// </summary>
    public IDisposable Begin(Guid? empresaId, Guid usuarioSolicitanteId, ReportContexto contexto)
    {
        if (IsSet)
            throw new InvalidOperationException(
                "Contexto de execução já ativo neste fluxo assíncrono. " +
                "Não aninhe chamadas Begin().");

        _frame.Value = new ReportExecutionFrame(empresaId, usuarioSolicitanteId, contexto);
        return new FrameDisposable();
    }

    private sealed class FrameDisposable : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _frame.Value = null;
            _disposed = true;
        }
    }
}

/// <summary>
/// Frame imutável do contexto de execução. Valores da run corrente.
/// </summary>
internal sealed record ReportExecutionFrame(
    Guid? EmpresaId,
    Guid UsuarioSolicitanteId,
    ReportContexto Contexto);

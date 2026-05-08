using EasyStock.Application.UseCases.Common;

namespace EasyStock.Application.UseCases.Fiscal.CancelarNotaFiscal;

public sealed record CancelarNotaFiscalCommand(
    Guid EmpresaId,
    Guid NotaFiscalId,
    string Justificativa,
    Guid? UsuarioId) : ICommand;

public sealed record CancelarNotaFiscalResult(
    Guid NotaFiscalId,
    string Status,
    string? ProtocoloCancelamento,
    DateTime? DhCancelamento);

using EasyStock.Application.UseCases.Common;

namespace EasyStock.Application.UseCases.Fiscal.ReprocessarContingencia;

public sealed record ReprocessarContingenciaCommand(
    Guid NotaFiscalId,
    Guid EmpresaId) : ICommand;

public sealed record ReprocessarContingenciaResult(
    Guid NotaFiscalId,
    string StatusFinal,
    string? Protocolo,
    string? Motivo);

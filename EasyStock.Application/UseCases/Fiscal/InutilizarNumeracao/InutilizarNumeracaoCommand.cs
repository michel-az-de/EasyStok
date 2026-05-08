using EasyStock.Application.UseCases.Common;

namespace EasyStock.Application.UseCases.Fiscal.InutilizarNumeracao;

public sealed record InutilizarNumeracaoCommand(
    Guid EmpresaId,
    Guid LojaId,
    int Serie,
    int NumeroInicial,
    int NumeroFinal,
    int Ano,
    string Justificativa,
    Guid? UsuarioId) : ICommand;

public sealed record InutilizarNumeracaoResult(
    Guid InutilizacaoId,
    string Status,
    string? Protocolo,
    string? Motivo);

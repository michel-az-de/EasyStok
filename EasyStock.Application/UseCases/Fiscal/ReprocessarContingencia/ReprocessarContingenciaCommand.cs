using EasyStock.Application.UseCases.Common;

namespace EasyStock.Application.UseCases.Fiscal.ReprocessarContingencia;

/// <summary>
/// Comando do job de contingencia. Sem tenant — opera cross-tenant via
/// <see cref="EasyStock.Application.Ports.Output.Security.IRowLevelSecurityBypass"/>.
/// </summary>
public sealed record ReprocessarContingenciaCommand(int BatchSize = 50) : ICommand;

public sealed record ReprocessarContingenciaResult(
    int Processadas,
    int Autorizadas,
    int Rejeitadas,
    int AindaTransientes);

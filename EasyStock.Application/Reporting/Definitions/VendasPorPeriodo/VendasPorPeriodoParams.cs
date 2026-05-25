namespace EasyStock.Application.Reporting.Definitions.VendasPorPeriodo;

/// <summary>
/// Parâmetros de entrada do relatório "Vendas por período".
/// </summary>
public sealed record VendasPorPeriodoParams(
    DateOnly De,
    DateOnly Ate,
    Guid? LojaId = null,
    string? FormaPagamento = null,
    Guid? VendedorId = null);

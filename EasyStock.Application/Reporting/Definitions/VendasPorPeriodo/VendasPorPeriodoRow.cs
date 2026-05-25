namespace EasyStock.Application.Reporting.Definitions.VendasPorPeriodo;

/// <summary>
/// Linha de saída do relatório "Vendas por período".
/// Colunas ordenadas conforme GetSchema() em VendasPorPeriodoDefinition.
///
/// NOTA DE DADOS LEGADOS:
/// Vendas criadas antes de PR-B não têm VendedorNome/FormaPagamentoPrincipal —
/// aparecem com null nas colunas correspondentes (exibido como "—" pelo exporter).
/// </summary>
public sealed record VendasPorPeriodoRow(
    DateTime DataVenda,
    string? NumeroNotaFiscal,
    string IdCurto,
    string? LojaNome,
    string? VendedorNome,
    string? FormaPagamentoPrincipal,
    int QtdItens,
    decimal Subtotal,
    decimal ValorDesconto,
    decimal ValorTotal);

namespace EasyStock.Application.Reporting.Definitions.Admin.Faturamento;

/// <summary>Linha do relatório de inadimplência — uma fatura vencida.</summary>
public sealed record InadimplenciaRow(
    string   EmpresaNome,
    string   FaturaNumero,
    DateTime DataVencimento,
    int      DiasAtraso,
    decimal  ValorTotal,
    decimal  ValorPago,
    decimal  SaldoDevedor,
    string   StatusFatura);

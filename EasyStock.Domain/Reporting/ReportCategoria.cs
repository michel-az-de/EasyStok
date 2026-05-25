namespace EasyStock.Domain.Reporting;

/// <summary>Categoria de negócio do relatório (usada para agrupamento no catálogo).</summary>
public enum ReportCategoria : short
{
    Vendas = 1,
    Estoque = 2,
    Fiscal = 3,
    Contabil = 4,
    AdminSaaS = 10,
}

namespace EasyStock.Application.Reporting.Definitions.Fiscal.TotalizadoresFiscais;

/// <summary>
/// Linha de saída do relatório "Totalizadores fiscais por CFOP/CST/NCM".
/// Cada linha é uma combinação única de CFOP × CST/CSOSN × NCM no período.
///
/// NOTA DE DADOS LEGADOS: NFC-e sem tributos rastreados (pré-PR-D) contribuem
/// apenas para QtdItens e TotalItens; os demais valores ficam como zero
/// e a coluna <see cref="TributosRastreados"/> indica false.
/// </summary>
public sealed record TotalizadoresFiscaisRow(
    string?  Cfop,
    string?  CstOuCsosn,
    string?  Ncm,
    int      QtdItens,
    decimal  TotalItens,
    decimal  BaseIcms,
    decimal  ValorIcms,
    decimal  Pis,
    decimal  Cofins,
    bool     TributosRastreados);

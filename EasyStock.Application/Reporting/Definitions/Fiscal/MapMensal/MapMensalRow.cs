namespace EasyStock.Application.Reporting.Definitions.Fiscal.MapMensal;

/// <summary>
/// Linha de saída do relatório "MAP — Mapa Resumo NFC-e".
/// Cada linha representa um dia de movimento, agregando NFC-e autorizadas e canceladas.
/// Formato esperado: XLSX (base para o MAP imprimível via QuestPDF em fase futura).
/// </summary>
public sealed record MapMensalRow(
    DateOnly Data,
    int      QtdAutorizadas,
    int      QtdCanceladas,
    decimal  TotalAutorizadas,
    decimal  TotalCanceladas,
    decimal  TotalLiquido,
    decimal  TotalIcms,
    decimal  TotalPis,
    decimal  TotalCofins);

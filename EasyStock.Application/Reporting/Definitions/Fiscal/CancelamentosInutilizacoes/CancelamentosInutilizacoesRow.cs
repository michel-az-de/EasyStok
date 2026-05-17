namespace EasyStock.Application.Reporting.Definitions.Fiscal.CancelamentosInutilizacoes;

/// <summary>
/// Linha de saída do relatório "Cancelamentos e inutilizações".
/// Cada linha representa um evento de cancelamento ou inutilização com seu protocolo SEFAZ.
/// </summary>
public sealed record CancelamentosInutilizacoesRow(
    DateTime  OcorridoEm,
    string    TipoEvento,
    long      Numero,
    short     Serie,
    string?   ChaveAcesso,
    decimal   TotalNota,
    string?   ProtocoloEvento,
    string?   UsuarioNome,
    string?   Origem);

using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities.Fiscal;
using EasyStock.Domain.Enums.Fiscal;

namespace EasyStock.Application.UseCases.Fiscal.ConsultarNotaFiscal;

public sealed record ConsultarNotaFiscalQuery(
    Guid EmpresaId,
    Guid? LojaId,
    DateTime? DesdeUtc,
    DateTime? AteUtc,
    StatusNotaFiscal? Status,
    string? ChaveAcesso,
    int Pagina = 1,
    int TamanhoPagina = 30) : ICommand;

public sealed record ConsultarNotaFiscalResult(
    IReadOnlyList<NotaFiscalListItem> Items,
    int Pagina,
    int TamanhoPagina,
    int TotalItens,
    int TotalPaginas);

public sealed record NotaFiscalListItem(
    Guid Id,
    string ChaveAcesso,
    int Numero,
    int Serie,
    short Modelo,
    string Status,
    DateTime DhEmi,
    DateTime? DhAutorizacao,
    decimal ValorTotal,
    Guid? LojaId,
    string? ClienteCpfMascarado,
    string TipoEmissao)
{
    public static NotaFiscalListItem From(NotaFiscal n) => new(
        Id: n.Id,
        ChaveAcesso: n.ChaveAcesso.Valor,
        Numero: n.Numero,
        Serie: n.Serie,
        Modelo: (short)n.Modelo,
        Status: n.Status.ToString(),
        DhEmi: n.DataEmissao,
        DhAutorizacao: n.DataAutorizacao,
        ValorTotal: n.ValorTotal.Valor,
        LojaId: n.LojaId,
        ClienteCpfMascarado: MascararCpf(n.ClienteCpfCnpj),
        TipoEmissao: n.TipoEmissao.ToString());

    private static string? MascararCpf(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        var d = new string(raw.Where(char.IsDigit).ToArray());
        return d.Length switch
        {
            11 => $"***.{d.Substring(3, 3)}.{d.Substring(6, 3)}-**",
            14 => $"**.***.{d.Substring(5, 3)}/{d.Substring(8, 4)}-**",
            _ => "***",
        };
    }
}

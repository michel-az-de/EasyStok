using EasyStock.Domain.Entities.Fiscal;

namespace EasyStock.Api.Dtos.Fiscal;

public sealed record NotaFiscalResponse(
    Guid Id,
    Guid EmpresaId,
    Guid? LojaId,
    Guid? PedidoId,
    string ChaveAcesso,
    string ChaveAcessoFormatada,
    int Numero,
    int Serie,
    short Modelo,
    string Status,
    string TipoEmissao,
    string Ambiente,
    DateTime DhEmi,
    DateTime? DhAutorizacao,
    DateTime? DhCancelamento,
    string? Protocolo,
    string? ProtocoloCancelamento,
    string? MotivoRejeicao,
    string? CodigoRejeicao,
    string? JustificativaCancelamento,
    string? ClienteCpfMascarado,
    decimal ValorTotal,
    bool EmContingencia,
    bool Arquivado,
    IReadOnlyList<NotaFiscalItemResponse> Itens,
    IReadOnlyList<NotaFiscalPagamentoResponse> Pagamentos)
{
    public static NotaFiscalResponse From(NotaFiscal n) => new(
        Id: n.Id,
        EmpresaId: n.EmpresaId,
        LojaId: n.LojaId,
        PedidoId: n.PedidoId,
        ChaveAcesso: n.ChaveAcesso.Valor,
        ChaveAcessoFormatada: n.ChaveAcesso.Formatada,
        Numero: n.Numero,
        Serie: n.Serie,
        Modelo: (short)n.Modelo,
        Status: n.Status.ToString(),
        TipoEmissao: n.TipoEmissao.ToString(),
        Ambiente: n.Ambiente.ToString(),
        DhEmi: n.DataEmissao,
        DhAutorizacao: n.DataAutorizacao,
        DhCancelamento: n.DataCancelamento,
        Protocolo: n.ProtocoloAutorizacao,
        ProtocoloCancelamento: n.ProtocoloCancelamento,
        MotivoRejeicao: n.MotivoRejeicao,
        CodigoRejeicao: n.CodigoRejeicao,
        JustificativaCancelamento: n.JustificativaCancelamento,
        ClienteCpfMascarado: MascararCpf(n.ClienteCpfCnpj),
        ValorTotal: n.ValorTotal.Valor,
        EmContingencia: n.Status == Domain.Enums.Fiscal.StatusNotaFiscal.EmContingencia,
        Arquivado: n.Arquivado,
        Itens: n.Itens.Select(NotaFiscalItemResponse.From).ToList(),
        Pagamentos: n.Pagamentos.Select(NotaFiscalPagamentoResponse.From).ToList());

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

public sealed record NotaFiscalItemResponse(
    Guid Id,
    int Ordem,
    string Descricao,
    string CodigoProduto,
    string? Ean,
    string Ncm,
    string Cfop,
    string? Cest,
    string Unidade,
    decimal Quantidade,
    decimal PrecoUnitario,
    decimal Desconto,
    decimal Subtotal,
    string CstCsosn)
{
    public static NotaFiscalItemResponse From(NotaFiscalItem i) => new(
        Id: i.Id,
        Ordem: i.Ordem,
        Descricao: i.DescricaoSnapshot,
        CodigoProduto: i.CodigoProduto,
        Ean: i.Ean,
        Ncm: i.Ncm.Valor,
        Cfop: i.Cfop.Valor,
        Cest: i.Cest,
        Unidade: i.UnidadeComercial,
        Quantidade: i.Quantidade,
        PrecoUnitario: i.PrecoUnitario,
        Desconto: i.Desconto,
        Subtotal: i.Subtotal.Valor,
        CstCsosn: i.CstCsosn.Valor);
}

public sealed record NotaFiscalPagamentoResponse(
    Guid Id,
    int Ordem,
    string FormaPagamento,
    decimal Valor,
    decimal Troco,
    string? BandeiraCartao,
    string? Nsu)
{
    public static NotaFiscalPagamentoResponse From(NotaFiscalPagamento p) => new(
        Id: p.Id,
        Ordem: p.Ordem,
        FormaPagamento: p.FormaPagamento.ToString(),
        Valor: p.Valor.Valor,
        Troco: p.Troco.Valor,
        BandeiraCartao: p.BandeiraCartao,
        Nsu: p.Nsu);
}

public sealed record InutilizacaoResponse(
    Guid Id,
    Guid LojaId,
    int Serie,
    int NumeroInicial,
    int NumeroFinal,
    int Ano,
    string Justificativa,
    string Status,
    string? Protocolo,
    DateTime CriadoEm)
{
    public static InutilizacaoResponse From(NotaFiscalInutilizacao i) => new(
        Id: i.Id,
        LojaId: i.LojaId,
        Serie: i.Serie,
        NumeroInicial: i.NumeroInicial,
        NumeroFinal: i.NumeroFinal,
        Ano: i.Ano,
        Justificativa: i.Justificativa,
        Status: i.Status.ToString(),
        Protocolo: i.ProtocoloInutilizacao,
        CriadoEm: i.CriadoEm);
}

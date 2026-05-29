using EasyStock.Domain.Entities.Financeiro;

namespace EasyStock.Application.UseCases.Financeiro.Common;

public sealed record CategoriaFinanceiraResult(
    Guid Id,
    Guid EmpresaId,
    string Nome,
    string Tipo,
    Guid? ParentId,
    int Profundidade,
    bool Ativa,
    string? Cor,
    string? Icone,
    int Ordem)
{
    public static CategoriaFinanceiraResult De(CategoriaFinanceira c) => new(
        c.Id, c.EmpresaId, c.Nome, c.Tipo.ToString(),
        c.ParentId, c.Profundidade, c.Ativa, c.Cor, c.Icone, c.Ordem);
}

public sealed record CentroCustoResult(
    Guid Id,
    Guid EmpresaId,
    Guid? LojaId,
    string Codigo,
    string Nome,
    string? Descricao,
    bool Ativo)
{
    public static CentroCustoResult De(CentroCusto c) => new(
        c.Id, c.EmpresaId, c.LojaId, c.Codigo, c.Nome, c.Descricao, c.Ativo);
}

public sealed record ParcelaResult(
    Guid Id,
    int Numero,
    decimal Valor,
    decimal ValorPago,
    decimal Saldo,
    DateTime DataVencimento,
    DateTime? DataPagamentoTotal,
    string Status,
    string? MetodoPlanejado,
    string? EfiTxid,
    string? PixCopiaCola,
    string? QrCodeBase64,
    DateTime? PixExpiraEm)
{
    public static ParcelaResult De(ParcelaPagar p) => new(
        p.Id, p.Numero, p.Valor, p.ValorPago, p.Saldo,
        p.DataVencimento, p.DataPagamentoTotal,
        p.Status.ToString(), p.MetodoPlanejado,
        EfiTxid: null, PixCopiaCola: null, QrCodeBase64: null, PixExpiraEm: null);

    public static ParcelaResult De(ParcelaReceber p) => new(
        p.Id, p.Numero, p.Valor, p.ValorPago, p.Saldo,
        p.DataVencimento, p.DataPagamentoTotal,
        p.Status.ToString(), p.MetodoPlanejado,
        p.EfiTxid, p.PixCopiaCola, p.QrCodeBase64, p.PixExpiraEm);
}

public sealed record PagamentoParcelaResult(
    Guid Id,
    string Lado,
    decimal Valor,
    string Metodo,
    string Status,
    DateTime DataPagamento,
    string? GatewayProvedor,
    string? GatewayTransactionId,
    Guid? MovimentoCaixaId,
    DateTime? EstornadoEm,
    string? MotivoEstorno,
    string? Observacao)
{
    public static PagamentoParcelaResult De(PagamentoParcela p) => new(
        p.Id, p.Lado.ToString(), p.Valor, p.Metodo, p.Status.ToString(),
        p.DataPagamento, p.GatewayProvedor, p.GatewayTransactionId,
        p.MovimentoCaixaId, p.EstornadoEm, p.MotivoEstorno, p.Observacao);
}

public sealed record ContaPagarResult(
    Guid Id,
    Guid EmpresaId,
    Guid? LojaId,
    Guid? FornecedorId,
    Guid CategoriaFinanceiraId,
    Guid? CentroCustoId,
    string Descricao,
    string? Observacoes,
    decimal ValorTotal,
    decimal TotalPago,
    decimal Pendente,
    string Status,
    DateTime DataEmissao,
    DateTime? DataCompetencia,
    string Origem,
    Guid? OrigemRefId,
    string? DocumentoReferencia,
    DateTime? CanceladaEm,
    string? MotivoCancelamento,
    DateTime CriadoEm,
    DateTime AlteradoEm,
    IReadOnlyList<ParcelaResult> Parcelas)
{
    public static ContaPagarResult De(ContaPagar c) => new(
        c.Id, c.EmpresaId, c.LojaId, c.FornecedorId, c.CategoriaFinanceiraId, c.CentroCustoId,
        c.Descricao, c.Observacoes,
        c.ValorTotal, c.TotalPago, c.Pendente,
        c.Status.ToString(),
        c.DataEmissao, c.DataCompetencia,
        c.Origem.ToString(), c.OrigemRefId, c.DocumentoReferencia,
        c.CanceladaEm, c.MotivoCancelamento,
        c.CriadoEm, c.AlteradoEm,
        c.Parcelas.OrderBy(p => p.Numero).Select(ParcelaResult.De).ToList());
}

public sealed record ContaReceberResult(
    Guid Id,
    Guid EmpresaId,
    Guid? LojaId,
    Guid? ClienteId,
    Guid CategoriaFinanceiraId,
    Guid? CentroCustoId,
    Guid? FaturaId,
    string Descricao,
    string? Observacoes,
    decimal ValorTotal,
    decimal TotalRecebido,
    decimal Pendente,
    string Status,
    DateTime DataEmissao,
    DateTime? DataCompetencia,
    string Origem,
    Guid? OrigemRefId,
    string? DocumentoReferencia,
    DateTime? CanceladaEm,
    string? MotivoCancelamento,
    DateTime CriadoEm,
    DateTime AlteradoEm,
    IReadOnlyList<ParcelaResult> Parcelas)
{
    public static ContaReceberResult De(ContaReceber c) => new(
        c.Id, c.EmpresaId, c.LojaId, c.ClienteId, c.CategoriaFinanceiraId, c.CentroCustoId,
        c.FaturaId, c.Descricao, c.Observacoes,
        c.ValorTotal, c.TotalRecebido, c.Pendente,
        c.Status.ToString(),
        c.DataEmissao, c.DataCompetencia,
        c.Origem.ToString(), c.OrigemRefId, c.DocumentoReferencia,
        c.CanceladaEm, c.MotivoCancelamento,
        c.CriadoEm, c.AlteradoEm,
        c.Parcelas.OrderBy(p => p.Numero).Select(ParcelaResult.De).ToList());
}

public sealed record ContasPagarPaginadasResult(
    IReadOnlyList<ContaPagarResult> Itens,
    int Total,
    int Page,
    int PageSize);

public sealed record ContasReceberPaginadasResult(
    IReadOnlyList<ContaReceberResult> Itens,
    int Total,
    int Page,
    int PageSize);

/// <summary>Especificacao de parcela pra criar uma conta com 1+N parcelas.</summary>
public sealed record ParcelaSpec(int Numero, decimal Valor, DateTime DataVencimento, string? MetodoPlanejado = null);

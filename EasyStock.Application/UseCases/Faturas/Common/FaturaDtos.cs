using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Application.UseCases.Faturas.Common;

/// <summary>DTO de listagem (sem itens detalhados).</summary>
public sealed record FaturaResumoDto(
    Guid Id,
    string Numero,
    Guid EmpresaId,
    string? EmpresaNome,
    string? FaturadoNome,
    string? FaturadoDocumento,
    string Origem,
    string Status,
    DateTime DataEmissao,
    DateTime DataVencimento,
    DateTime? DataPagamentoTotal,
    decimal Total,
    decimal TotalPago,
    decimal Pendente,
    string Moeda
)
{
    public static FaturaResumoDto FromEntity(Fatura f) => new(
        f.Id,
        f.Numero,
        f.EmpresaId,
        f.Empresa?.Nome,
        f.DadosFaturado?.Nome,
        f.DadosFaturado?.Documento,
        f.Origem.ToString(),
        f.Status.ToString(),
        f.DataEmissao,
        f.DataVencimento,
        f.DataPagamentoTotal,
        f.Total,
        f.TotalPago,
        f.Pendente,
        f.Moeda
    );
}

public sealed record FaturaItemDto(
    Guid Id,
    string Descricao,
    decimal Quantidade,
    decimal PrecoUnitario,
    decimal Subtotal,
    string Tipo,
    int Ordem
)
{
    public static FaturaItemDto FromEntity(FaturaItem i) => new(
        i.Id, i.Descricao, i.Quantidade, i.PrecoUnitario, i.Subtotal, i.Tipo.ToString(), i.Ordem
    );
}

public sealed record FaturaPagamentoDto(
    Guid Id,
    string Metodo,
    decimal Valor,
    string Status,
    string GatewayProvedor,
    string? GatewayTransactionId,
    DateTime? PagoEm,
    DateTime CriadoEm,
    string? Observacao,
    string? RegistradoPorNome,
    string? DadosGatewayJson
)
{
    public static FaturaPagamentoDto FromEntity(FaturaPagamento p) => new(
        p.Id, p.Metodo, p.Valor, p.Status.ToString(), p.GatewayProvedor,
        p.GatewayTransactionId, p.PagoEm, p.CriadoEm, p.Observacao,
        p.RegistradoPorNome, p.DadosGatewayJson
    );
}

public sealed record FaturaEventoDto(
    Guid Id,
    string Tipo,
    string? ValorAntes,
    string? ValorDepois,
    string? UsuarioNome,
    Guid? UsuarioId,
    string? Origem,
    string? MetadadosJson,
    DateTime OcorridoEm
)
{
    public static FaturaEventoDto FromEntity(FaturaEvento e) => new(
        e.Id, e.Tipo.ToString(), e.ValorAntes, e.ValorDepois,
        e.UsuarioNome, e.UsuarioId, e.Origem, e.MetadadosJson, e.OcorridoEm
    );
}

public sealed record FaturaDetalheDto(
    Guid Id,
    string Numero,
    Guid EmpresaId,
    string? EmpresaNome,
    Guid? ClienteId,
    DadosFaturado DadosFaturado,
    DadosEmissor DadosEmissor,
    DadosFiscais? DadosFiscais,
    string Origem,
    Guid? OrigemRefId,
    string Status,
    DateTime DataEmissao,
    DateTime DataVencimento,
    DateTime? DataPagamentoTotal,
    decimal SubTotal,
    decimal Descontos,
    decimal Acrescimos,
    decimal Total,
    decimal TotalPago,
    decimal Pendente,
    string Moeda,
    string? Observacoes,
    Guid? TicketRelacionadoId,
    string? PdfStorageKey,
    DateTime CriadoEm,
    DateTime AlteradoEm,
    IReadOnlyList<FaturaItemDto> Itens,
    IReadOnlyList<FaturaPagamentoDto> Pagamentos,
    IReadOnlyList<FaturaEventoDto> Eventos
)
{
    public static FaturaDetalheDto FromEntity(Fatura f) => new(
        f.Id, f.Numero, f.EmpresaId, f.Empresa?.Nome, f.ClienteId,
        f.DadosFaturado, f.DadosEmissor, f.DadosFiscais,
        f.Origem.ToString(), f.OrigemRefId,
        f.Status.ToString(),
        f.DataEmissao, f.DataVencimento, f.DataPagamentoTotal,
        f.SubTotal, f.Descontos, f.Acrescimos, f.Total,
        f.TotalPago, f.Pendente,
        f.Moeda, f.Observacoes, f.TicketRelacionadoId, f.PdfStorageKey,
        f.CriadoEm, f.AlteradoEm,
        f.Itens.Select(FaturaItemDto.FromEntity).ToList(),
        f.Pagamentos.Select(FaturaPagamentoDto.FromEntity).ToList(),
        f.Eventos.Select(FaturaEventoDto.FromEntity).ToList()
    );
}

/// <summary>Input de item para emissao de fatura avulsa.</summary>
public sealed record FaturaItemInput(
    string Descricao,
    decimal Quantidade,
    decimal PrecoUnitario,
    TipoItemFatura Tipo = TipoItemFatura.Servico,
    Guid? ProdutoId = null
);

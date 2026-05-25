using System.Text.Json.Serialization;

namespace EasyStok.Mobile.Models;

/// <summary>
/// Body de POST /api/estoque/entrada — espelha
/// <c>RegistrarEntradaEstoqueCommand</c> em EasyStock.Application.
/// Mantemos so os campos minimos necessarios pra contagem de producao;
/// outros (codigos, fornecedor, etc) ficam null e o backend usa default.
/// </summary>
public sealed record RegistrarEntradaCommand(
    [property: JsonPropertyName("empresaId")] Guid EmpresaId,
    [property: JsonPropertyName("produtoId")] Guid ProdutoId,
    [property: JsonPropertyName("produtoVariacaoId")] Guid? ProdutoVariacaoId,
    [property: JsonPropertyName("quantidade")] int Quantidade,
    [property: JsonPropertyName("custoUnitario")] decimal CustoUnitario,
    [property: JsonPropertyName("precoVendaSugerido")] decimal? PrecoVendaSugerido,
    [property: JsonPropertyName("dataEntrada")] DateTime DataEntrada,
    [property: JsonPropertyName("natureza")] string Natureza,
    [property: JsonPropertyName("codigoInterno")] string? CodigoInterno,
    [property: JsonPropertyName("codigoLote")] string? CodigoLote,
    [property: JsonPropertyName("codigoMarketplace")] string? CodigoMarketplace,
    [property: JsonPropertyName("variacaoDescricao")] string? VariacaoDescricao,
    [property: JsonPropertyName("cor")] string? Cor,
    [property: JsonPropertyName("tamanho")] string? Tamanho,
    [property: JsonPropertyName("fornecedorNome")] string? FornecedorNome,
    [property: JsonPropertyName("validade")] DateTime? Validade,
    [property: JsonPropertyName("observacoes")] string? Observacoes,
    [property: JsonPropertyName("descricaoAnuncio")] string? DescricaoAnuncio,
    [property: JsonPropertyName("documentoReferencia")] string? DocumentoReferencia,
    [property: JsonPropertyName("dimensoesReais")] object? DimensoesReais,
    [property: JsonPropertyName("instrucoesGeracaoDescricao")] string? InstrucoesGeracaoDescricao,
    [property: JsonPropertyName("lojaId")] Guid? LojaId);

/// <summary>
/// Item da saida (POST /api/estoque/saida).
/// </summary>
public sealed record RegistrarSaidaItem(
    [property: JsonPropertyName("itemEstoqueId")] Guid? ItemEstoqueId,
    [property: JsonPropertyName("produtoId")] Guid ProdutoId,
    [property: JsonPropertyName("produtoVariacaoId")] Guid? ProdutoVariacaoId,
    [property: JsonPropertyName("quantidade")] int Quantidade,
    [property: JsonPropertyName("valorVendaUnitario")] decimal ValorVendaUnitario,
    [property: JsonPropertyName("descricao")] string? Descricao);

/// <summary>
/// Body de POST /api/estoque/saida — espelha
/// <c>RegistrarSaidaEstoqueCommand</c>. Suporta multiplos itens; pra
/// botao "-1" da producao mandamos um item so.
/// </summary>
public sealed record RegistrarSaidaCommand(
    [property: JsonPropertyName("empresaId")] Guid EmpresaId,
    [property: JsonPropertyName("itens")] IReadOnlyList<RegistrarSaidaItem> Itens,
    [property: JsonPropertyName("dataVenda")] DateTime DataVenda,
    [property: JsonPropertyName("dataSaida")] DateTime DataSaida,
    [property: JsonPropertyName("dataEnvio")] DateTime? DataEnvio,
    [property: JsonPropertyName("notaFiscal")] string? NotaFiscal,
    [property: JsonPropertyName("natureza")] string Natureza,
    [property: JsonPropertyName("canal")] string Canal,
    [property: JsonPropertyName("observacoes")] string? Observacoes);

/// <summary>
/// Tipos de mutation persistidos no outbox.
/// </summary>
public static class OutboxTypes
{
    public const string EstoqueEntrada = "estoque.entrada";
    public const string EstoqueSaida = "estoque.saida";
}

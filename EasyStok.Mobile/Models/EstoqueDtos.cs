using System.Text.Json.Serialization;

namespace EasyStok.Mobile.Models;

/// <summary>
/// Resposta paginada padrao da API: <c>{ data: [...], meta: { total, pages, page, limit } }</c>.
/// </summary>
public sealed record EnvelopePaginado<T>(
    [property: JsonPropertyName("data")] List<T> Data,
    [property: JsonPropertyName("meta")] MetaPaginacao Meta);

public sealed record MetaPaginacao(
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("pages")] int Pages,
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("limit")] int Limit);

/// <summary>
/// Espelha o anonymous DTO de <c>ItemEstoqueController.MapItemToDto</c>.
/// Strings em vez de Guid em <c>id</c>/<c>produtoId</c>/<c>varId</c> para
/// bater com a serializacao do backend (i.Id.ToString()).
/// </summary>
public sealed record ItemEstoqueRemoto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("produtoId")] string ProdutoId,
    [property: JsonPropertyName("varId")] string? VarId,
    [property: JsonPropertyName("sku")] string Sku,
    [property: JsonPropertyName("qty")] int Qty,
    [property: JsonPropertyName("entryDate")] DateOnly EntryDate,
    [property: JsonPropertyName("lastMov")] DateTimeOffset LastMov,
    [property: JsonPropertyName("validade")] DateOnly? Validade,
    [property: JsonPropertyName("lote")] string? Lote,
    [property: JsonPropertyName("vel")] decimal Vel,
    [property: JsonPropertyName("stopped")] int Stopped,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("custoUnitario")] decimal CustoUnitario,
    [property: JsonPropertyName("precoVendaSugerido")] decimal? PrecoVendaSugerido,
    [property: JsonPropertyName("produto")] ProdutoLeve? Produto,
    [property: JsonPropertyName("variacao")] VariacaoLeve? Variacao);

public sealed record ProdutoLeve(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("sku")] string Sku,
    [property: JsonPropertyName("nome")] string Nome,
    [property: JsonPropertyName("emoji")] string? Emoji,
    [property: JsonPropertyName("categoria")] string? Categoria,
    [property: JsonPropertyName("status")] string Status);

public sealed record VariacaoLeve(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("produtoId")] string ProdutoId,
    [property: JsonPropertyName("nome")] string Nome);

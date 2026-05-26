namespace EasyStock.Application.UseCases.Storefront.Menu;

/// <summary>
/// Resultado do <see cref="ListarCardapioPublicoUseCase"/>. Lista vazia é
/// resposta válida (storefront ativo, mas sem items visíveis ainda).
/// </summary>
public sealed record ListarCardapioPublicoResult(
    IReadOnlyList<CardapioItemPublicoDto> Itens);

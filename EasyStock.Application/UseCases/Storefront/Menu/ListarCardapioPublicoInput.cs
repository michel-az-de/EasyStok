namespace EasyStock.Application.UseCases.Storefront.Menu;

/// <summary>
/// Input do <see cref="ListarCardapioPublicoUseCase"/>. Identifica o storefront
/// pelo slug público. Sem PII — endpoint anônimo.
/// </summary>
public sealed record ListarCardapioPublicoInput(string Slug);

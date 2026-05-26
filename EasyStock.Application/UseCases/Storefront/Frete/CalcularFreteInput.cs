namespace EasyStock.Application.UseCases.Storefront.Frete;

/// <summary>
/// Entrada do <see cref="CalcularFreteUseCase"/>.
///
/// <list type="bullet">
///   <item><see cref="Slug"/>: identificador público do storefront.</item>
///   <item><see cref="Cep"/>: CEP bruto digitado pelo cliente (com ou sem máscara).</item>
/// </list>
/// </summary>
public sealed record CalcularFreteInput(
    string Slug,
    string Cep);

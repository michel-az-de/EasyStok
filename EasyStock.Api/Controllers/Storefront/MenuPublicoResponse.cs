using EasyStock.Application.UseCases.Storefront.Menu;

namespace EasyStock.Api.Controllers.Storefront;

/// <summary>
/// Envelope de resposta do cardápio público (<c>GET /api/storefront/{slug}/menu</c>).
///
/// <para>
/// Contrato canônico <c>menu-publico.contract.md</c>: a vitrine Casa da Babá
/// (<c>menu.js</c>) exige <c>{ "itens": [...] }</c> — <c>Array.isArray(data.itens)</c>.
/// Reusa <see cref="CardapioItemPublicoDto"/> (projeção pública medida: sem campos
/// internos, 10 chaves, nulls emitidos) — não introduz item type paralelo.
/// </para>
///
/// <para>
/// <see cref="TituloPublico"/> e <see cref="Slug"/> são campos públicos seguros
/// (slug já vem na URL) e enriquecem o cabeçalho da vitrine; aditivos, não quebram
/// o parser que só requer <c>itens</c>.
/// </para>
/// </summary>
public sealed record MenuPublicoResponse(
    IReadOnlyList<CardapioItemPublicoDto> Itens,
    string TituloPublico,
    string Slug);

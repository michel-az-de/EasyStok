namespace EasyStock.Application.UseCases.Storefront.Menu;

/// <summary>
/// Resultado do <see cref="ListarCardapioPublicoUseCase"/>. Lista vazia é
/// resposta válida (storefront ativo, mas sem items visíveis ainda).
///
/// <para><see cref="TituloPublico"/> e <see cref="Slug"/> servem ao endpoint de
/// impressão (cabeçalho do HTML) E ao endpoint JSON público, que desde a fatia
/// "envelope" (#643) serializa o objeto inteiro como <c>{ itens, tituloPublico, slug }</c>
/// (contrato canônico <c>menu-publico.contract.md</c> que a vitrine consome). O ETag é o
/// hash desse payload-envelope — determinístico, com desempate <c>CriadoEm → Id</c> na
/// ordenação do use case.</para>
/// </summary>
public sealed record ListarCardapioPublicoResult(
    IReadOnlyList<CardapioItemPublicoDto> Itens,
    string TituloPublico = "",
    string Slug = "");

namespace EasyStock.Application.UseCases.Storefront.Menu;

/// <summary>
/// Resultado do <see cref="ListarCardapioPublicoUseCase"/>. Lista vazia é
/// resposta válida (storefront ativo, mas sem items visíveis ainda).
///
/// <para><see cref="TituloPublico"/> e <see cref="Slug"/> servem ao endpoint de
/// impressão (cabeçalho do HTML). O endpoint JSON público serializa apenas
/// <see cref="Itens"/>, então o ETag e o contrato com clientes não mudam.</para>
/// </summary>
public sealed record ListarCardapioPublicoResult(
    IReadOnlyList<CardapioItemPublicoDto> Itens,
    string TituloPublico = "",
    string Slug = "");

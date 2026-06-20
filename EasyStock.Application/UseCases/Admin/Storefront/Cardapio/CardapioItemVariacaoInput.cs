namespace EasyStock.Application.UseCases.Admin.Storefront.Cardapio;

/// <summary>
/// Entrada de uma opção do item guarda-chuva na autoria admin (ADR-0035, #652).
/// Usada por Adicionar/Editar. Na edição, <see cref="Id"/> dirige a reconciliação
/// keyed-by-Id: preenchido = atualiza a opção existente; null = nova opção.
///
/// <para>O link a <c>ProdutoVariacao</c> do ERP (rastreabilidade) fica para um follow-up
/// (exige validação de escopo empresa/produto); por ora as opções são storefront-side
/// (rótulo + preço próprios), cobrindo o caso avulso (ex.: Ravioli 300g / 800g).</para>
/// </summary>
public sealed record CardapioItemVariacaoInput(
    Guid? Id,
    string Rotulo,
    decimal PrecoStorefront,
    bool Disponivel = true,
    bool EhPadrao = false,
    string? PesoExibicao = null,
    string? Sku = null,
    double OrdemExibicao = 0);

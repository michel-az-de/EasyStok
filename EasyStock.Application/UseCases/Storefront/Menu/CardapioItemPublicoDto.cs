namespace EasyStock.Application.UseCases.Storefront.Menu;

/// <summary>
/// Projeção pública de um item do cardápio. Contém apenas campos seguros para
/// exposição anônima — sem <c>EmpresaId</c>, <c>CustoReferencia</c>, <c>MargemEstimada</c>,
/// <c>FornecedorId</c>.
///
/// <para>
/// <strong>Preço em centavos</strong> (long) para evitar floating-point no transit.
/// Cliente converte para BRL na UI.
/// </para>
///
/// <para>
/// <strong>EstoqueAtual</strong> é snapshot eventual — pode estar desatualizado
/// pela cache HTTP de 5 min. Não usar para decisão de compra; apenas para sinal
/// visual ("esgotado" via <see cref="Disponivel"/>).
/// Null para itens avulsos (sem vínculo com ERP); frontend usa <see cref="Disponivel"/>
/// como sinal canônico (esgotado = disponivel === false).
/// </para>
/// </summary>
public sealed record CardapioItemPublicoDto(
    Guid Id,
    string Nome,
    string? Descricao,
    long PrecoCentavos,
    string? ImagemUrl,
    int? EstoqueAtual,   // null = avulso (sem inventário ERP)
    string? Categoria,
    double Ordem,
    bool Disponivel,
    string? Tag);

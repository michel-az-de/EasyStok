using EasyStock.Domain.Defaults;

namespace EasyStock.Domain.Services;

/// <summary>
/// Resolve os limiares (mínimo e crítico) que governam o status de um <see cref="ItemEstoque"/>
/// seguindo a hierarquia: Produto -> Categoria -> ConfiguracaoLoja -> Default global.
/// O primeiro nível com valor não-nulo vence; nulos descem para o próximo nível.
/// </summary>
public static class LimiarEstoqueResolver
{
    public readonly record struct Limiares(int QuantidadeMinima, int QuantidadeCritica);

    public static Limiares Resolver(
        Produto? produto,
        Categoria? categoria,
        ConfiguracaoLoja? configuracaoLoja)
        => Resolver(
            produto?.QuantidadeMinima, produto?.QuantidadeCritica,
            categoria?.QuantidadeMinima, categoria?.QuantidadeCritica,
            configuracaoLoja?.QuantidadeMinimaPadrao, configuracaoLoja?.QuantidadeCriticaPadrao);

    /// <summary>
    /// Overload por valores brutos (sem entidades): mesma hierarquia e mesma normalização.
    /// Usado pela projeção de reposição (ADR-0039) para resolver o limiar AO VIVO e pela
    /// função pura <see cref="AnalisadorReposicao"/>, mantendo paridade com o SQL.
    /// </summary>
    public static Limiares Resolver(
        int? produtoMinima, int? produtoCritica,
        int? categoriaMinima, int? categoriaCritica,
        int? configMinima, int? configCritica)
    {
        var minima =
            produtoMinima
            ?? categoriaMinima
            ?? configMinima
            ?? OperacionalDefaults.QuantidadeMinima;

        var critica =
            produtoCritica
            ?? categoriaCritica
            ?? configCritica
            ?? OperacionalDefaults.QuantidadeCritica;

        // Garantia de invariante: critica nunca pode ser >= minima.
        if (critica >= minima)
            critica = Math.Max(0, minima - 1);

        return new Limiares(minima, critica);
    }
}

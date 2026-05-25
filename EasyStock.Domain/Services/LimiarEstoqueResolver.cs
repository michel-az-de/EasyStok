using EasyStock.Domain.Defaults;
using EasyStock.Domain.Entities;

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
    {
        var minima =
            produto?.QuantidadeMinima
            ?? categoria?.QuantidadeMinima
            ?? configuracaoLoja?.QuantidadeMinimaPadrao
            ?? OperacionalDefaults.QuantidadeMinima;

        var critica =
            produto?.QuantidadeCritica
            ?? categoria?.QuantidadeCritica
            ?? configuracaoLoja?.QuantidadeCriticaPadrao
            ?? OperacionalDefaults.QuantidadeCritica;

        // Garantia de invariante: critica nunca pode ser >= minima.
        if (critica >= minima)
            critica = Math.Max(0, minima - 1);

        return new Limiares(minima, critica);
    }
}

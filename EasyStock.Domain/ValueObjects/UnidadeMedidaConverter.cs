using EasyStock.Domain.Enums;

namespace EasyStock.Domain.ValueObjects;

/// <summary>
/// Conversao de valores entre <see cref="UnidadeMedida"/>s do mesmo grupo (massa / volume / contagem).
/// Grupos cruzados retornam falha — calculadora marca a linha como inconvertivel mas nao falha o calculo todo.
/// Cx nunca converte (tamanho variavel por produto — evolucao futura via Produto.UnidadesPorCaixa).
/// </summary>
public static class UnidadeMedidaConverter
{
    public enum GrupoUnidade
    {
        Massa,
        Volume,
        Contagem
    }

    /// <summary>Fator para converter de uma unidade para a referencia do grupo (g para massa, ml para volume, un para contagem).</summary>
    private static readonly Dictionary<UnidadeMedida, (GrupoUnidade Grupo, decimal FatorParaReferencia)> _fatores = new()
    {
        [UnidadeMedida.Mg] = (GrupoUnidade.Massa,    0.001m),
        [UnidadeMedida.G]  = (GrupoUnidade.Massa,    1m),
        [UnidadeMedida.Kg] = (GrupoUnidade.Massa,    1000m),
        [UnidadeMedida.Ml] = (GrupoUnidade.Volume,   1m),
        [UnidadeMedida.L]  = (GrupoUnidade.Volume,   1000m),
        [UnidadeMedida.Un] = (GrupoUnidade.Contagem, 1m),
        [UnidadeMedida.Dz] = (GrupoUnidade.Contagem, 12m),
        // Cx nao entra: tratado a parte como inconvertivel.
    };

    public static GrupoUnidade? GetGrupo(UnidadeMedida u)
        => _fatores.TryGetValue(u, out var info) ? info.Grupo : null;

    /// <summary>
    /// Converte <paramref name="valor"/> de <paramref name="de"/> para <paramref name="para"/>.
    /// Retorna (valor convertido, null) em sucesso; (null, motivo) em falha.
    /// </summary>
    public static (decimal? Convertido, string? Erro) Converter(decimal valor, UnidadeMedida de, UnidadeMedida para)
    {
        if (de == para)
            return (valor, null);

        if (de == UnidadeMedida.Cx || para == UnidadeMedida.Cx)
            return (null, "Cx nao converte automaticamente (tamanho variavel por produto).");

        if (!_fatores.TryGetValue(de, out var info_de) || !_fatores.TryGetValue(para, out var info_para))
            return (null, "Unidade desconhecida.");

        if (info_de.Grupo != info_para.Grupo)
            return (null, $"Grupos incompativeis: {info_de.Grupo} vs {info_para.Grupo}.");

        var valorReferencia = valor * info_de.FatorParaReferencia;
        var convertido = valorReferencia / info_para.FatorParaReferencia;
        return (convertido, null);
    }

    /// <summary>
    /// Lista as unidades compativeis com <paramref name="baseUnidade"/> (mesmo grupo).
    /// Usado pela UI para filtrar chips de unidade desejada na calculadora (G7).
    /// Cx aparece se a base for Cx — nao mistura com outras.
    /// </summary>
    public static IReadOnlyList<UnidadeMedida> UnidadesCompativeis(UnidadeMedida baseUnidade)
    {
        if (baseUnidade == UnidadeMedida.Cx)
            return [UnidadeMedida.Cx];

        var grupo = GetGrupo(baseUnidade);
        if (grupo == null)
            return [baseUnidade];

        return _fatores
            .Where(kv => kv.Value.Grupo == grupo)
            .Select(kv => kv.Key)
            .ToList();
    }
}

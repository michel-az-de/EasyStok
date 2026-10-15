namespace EasyStock.Infra.Postgre.Data;

/// <summary>
/// #1639: o startup aplicava cada pendente com <c>IMigrator.MigrateAsync(id)</c>. Migrar PARA um alvo
/// reverte tudo o que foi aplicado depois dele; uma migração de carimbo antigo (PR que ficou parada)
/// faria o banco rodar o <c>Down</c> de todas as posteriores e perder as colunas delas. Quem aplica
/// precisa saber quais pendentes não podem servir de alvo.
/// </summary>
public static class MigracoesPendentes
{
    /// <summary>
    /// Pendentes com carimbo anterior à última aplicada, em ordem. O id do EF começa pelo carimbo
    /// (<c>yyyyMMddHHmmss_Nome</c>), então a comparação ordinal é a ordem de aplicação.
    /// </summary>
    public static IReadOnlyList<string> ForaDeOrdem(IEnumerable<string> aplicadas, IEnumerable<string> pendentes)
    {
        var ultimaAplicada = aplicadas.Max(StringComparer.Ordinal);
        if (ultimaAplicada is null) return [];
        return pendentes
            .Where(pendente => string.CompareOrdinal(pendente, ultimaAplicada) < 0)
            .OrderBy(pendente => pendente, StringComparer.Ordinal)
            .ToList();
    }
}

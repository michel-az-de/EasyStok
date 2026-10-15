namespace EasyStock.Infra.Postgre.Configuration;

/// <summary>
/// Configuracoes de TTL para o cache Redis nos repositorios PostgreSQL.
/// Configure via secao "Cache" no appsettings.json.
/// </summary>
public sealed class CacheOptions
{
    /// <summary>
    /// Tempo de vida das entradas de paginacao de produtos.
    /// Padrao: 5 minutos.
    /// </summary>
    public TimeSpan ProdutosPaginadosDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Tempo de vida da chave de versao usada para invalidacao do cache de produtos.
    /// Padrao: 5 minutos.
    /// </summary>
    public TimeSpan ProdutosVersaoDuration { get; set; } = TimeSpan.FromMinutes(5);
}

namespace EasyStock.Application.UseCases.Common;

/// <summary>
/// Centraliza as chaves de cache utilizadas na aplicação.
/// Todas as chaves seguem o padrão: {modulo}:{empresaId}:{recurso}:{id?}
/// </summary>
public static class CacheKeys
{
    public static string Produto(Guid empresaId, Guid produtoId) =>
        $"produto:{empresaId}:{produtoId}";

    public static string ProdutoListagem(Guid empresaId) =>
        $"produto:listagem:{empresaId}";

    public static string ProdutoEstatisticas(Guid empresaId, Guid produtoId) =>
        $"produto:stats:{empresaId}:{produtoId}";

    public static string ItemEstoque(Guid empresaId, Guid itemId) =>
        $"estoque:item:{empresaId}:{itemId}";

    public static string EstoqueResumo(Guid empresaId) =>
        $"estoque:resumo:{empresaId}";

    public static string Dashboard(Guid empresaId) =>
        $"dashboard:{empresaId}";

    public static string Fornecedor(Guid empresaId, Guid fornecedorId) =>
        $"fornecedor:{empresaId}:{fornecedorId}";

    public static string FornecedorListagem(Guid empresaId) =>
        $"fornecedor:listagem:{empresaId}";

    public static string ConfiguracaoLoja(Guid lojaId) =>
        $"config:loja:{lojaId}";

    public static string Categoria(Guid empresaId) =>
        $"categoria:listagem:{empresaId}";

    public static string PlanoListagem() =>
        "plano:listagem";

    /// <summary>
    /// Retorna todas as chaves de cache relacionadas a um produto específico.
    /// </summary>
    public static IEnumerable<string> ProdutoRelacionadas(Guid empresaId, Guid produtoId)
    {
        yield return Produto(empresaId, produtoId);
        yield return ProdutoListagem(empresaId);
        yield return ProdutoEstatisticas(empresaId, produtoId);
        yield return Dashboard(empresaId);
    }
}

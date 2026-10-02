namespace EasyStock.Application.UseCases.Common;

/// <summary>
/// Centraliza as chaves de cache utilizadas na aplicação.
/// As chaves seguem convenções por contexto, normalmente no formato
/// {modulo}:{recurso}:{empresaId}:{id?} ou variações equivalentes.
/// Algumas chaves são globais (sem escopo de empresa), como plano:listagem,
/// e outras utilizam identificadores específicos do domínio, como lojaId.
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
    /// Sessão do usuário para o validador do JWT (#1352): valor <c>SessaoDoUsuario</c> por 60 s. Chave global
    /// (usuário não tem empresa) e versionada: mudar o formato do valor sobe o <c>v1</c>.
    /// </summary>
    public static string Sessao(Guid usuarioId) =>
        $"sessao:v1:{usuarioId:N}";

    /// <summary>Validade da entrada <see cref="Sessao"/>: teto de quanto uma revogação demora entre réplicas sem Redis.</summary>
    public static readonly TimeSpan SessaoTtl = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Retorna todas as chaves de cache relacionadas a um produto específico.
    /// </summary>
    public static IReadOnlyList<string> ProdutoRelacionadas(Guid empresaId, Guid produtoId) =>
    [
        Produto(empresaId, produtoId),
        ProdutoListagem(empresaId),
        ProdutoEstatisticas(empresaId, produtoId),
        Dashboard(empresaId),
    ];
}

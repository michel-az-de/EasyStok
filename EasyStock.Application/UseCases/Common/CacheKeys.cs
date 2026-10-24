namespace EasyStock.Application.UseCases.Common;

/// <summary>
/// Centraliza as chaves de cache utilizadas na aplicação.
/// As chaves seguem convenções por contexto, normalmente no formato
/// {modulo}:{recurso}:{empresaId}:{id?} ou variações equivalentes.
/// Algumas chaves são globais (sem escopo de empresa), como sessao:v1,
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

    public static string Dashboard(Guid empresaId) =>
        $"dashboard:{empresaId}";

    /// <summary>
    /// Sessão do usuário para o validador do JWT (#1352): valor <c>SessaoDoUsuario</c> por 60 s. Chave global
    /// (usuário não tem empresa) e versionada: mudar o formato do valor sobe o <c>v1</c>.
    /// </summary>
    public static string Sessao(Guid usuarioId) =>
        $"sessao:v1:{usuarioId:N}";

    /// <summary>Validade da entrada <see cref="Sessao"/>: teto de quanto uma revogação demora entre réplicas sem Redis.</summary>
    public static readonly TimeSpan SessaoTtl = TimeSpan.FromSeconds(60);

    /// <summary>
    /// SLA de primeira resposta que a inbox do atendimento repete em toda linha (#1566): minutos efetivos, por empresa.
    /// Salvar a configuração apaga a chave; sem cache compartilhado entre réplicas o valor novo aparece em até
    /// <see cref="AtendimentoSlaRespostaTtl"/>.
    /// </summary>
    public static string AtendimentoSlaResposta(Guid empresaId) =>
        $"atendimento:sla-resposta:{empresaId}";

    public static readonly TimeSpan AtendimentoSlaRespostaTtl = TimeSpan.FromSeconds(60);

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

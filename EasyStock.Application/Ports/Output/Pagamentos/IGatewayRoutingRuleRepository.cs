using EasyStock.Domain.Entities.Pagamentos;

namespace EasyStock.Application.Ports.Output.Pagamentos;

/// <summary>
/// Repositorio de <see cref="GatewayRoutingRule"/>. Tipo isento do Global
/// Query Filter (<c>EmpresaId</c> nullable; NULL = regra global). Repository
/// filtra manualmente <c>EmpresaId == tenant OR EmpresaId IS NULL</c>.
///
/// <para>
/// Implementacao usa <c>IMemoryCache</c> com TTL 60s — admin alterar regra
/// demora ate 60s pra propagar.
/// </para>
/// </summary>
public interface IGatewayRoutingRuleRepository
{
    /// <summary>
    /// Retorna regras aplicaveis (ativas, batendo metodo+moeda+pais), incluindo
    /// regras tenant-especificas e regras globais. Caller (router) decide
    /// precedencia.
    /// </summary>
    Task<IReadOnlyList<GatewayRoutingRule>> ObterRegrasAplicaveisAsync(
        Guid empresaId,
        string metodo,
        string moeda = "BRL",
        string pais = "BR",
        CancellationToken ct = default);

    /// <summary>Invalida cache para um tenant — chamado por admin ao alterar regras.</summary>
    void InvalidarCache(Guid? empresaId);
}

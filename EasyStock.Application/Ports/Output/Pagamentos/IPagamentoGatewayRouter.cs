namespace EasyStock.Application.Ports.Output.Pagamentos;

/// <summary>
/// Resolve qual <see cref="IPagamentoGateway"/> usar dado o contexto
/// (empresa + metodo de pagamento solicitado).
///
/// <para>
/// <b>Onda P0 Payment Orchestration</b>: o router consulta
/// <c>GatewayRoutingRule</c> persistida (cache 60s) — regras tenant-especificas
/// tem precedencia sobre globais. <see cref="PlanejarRotaAsync"/> retorna a
/// LISTA ordenada (preferido primeiro) para o orchestrator iterar quando
/// fallback estiver habilitado (P1).
/// </para>
///
/// <para>
/// <b>Compat</b>: <see cref="Resolver"/> e <see cref="ResolverPorProvedor"/>
/// sao mantidos para callers existentes (webhooks). <see cref="Resolver"/>
/// internamente delega ao <see cref="PlanejarRotaAsync"/> e retorna o primeiro
/// provedor candidato como <see cref="IPagamentoGateway"/>.
/// </para>
/// </summary>
public interface IPagamentoGatewayRouter
{
    /// <summary>
    /// Resolve o gateway para uma <c>(empresaId, metodo)</c>. Retorna null
    /// se nenhum gateway suportar o metodo na configuracao atual — caller
    /// deve tratar como "metodo indisponivel". Para multi-tentativa com
    /// fallback, prefira <see cref="PlanejarRotaAsync"/>.
    /// </summary>
    IPagamentoGateway? Resolver(Guid empresaId, string metodo);

    /// <summary>Resolve por nome explicito do provedor — usado para webhooks.</summary>
    IPagamentoGateway? ResolverPorProvedor(string provedor);

    /// <summary>
    /// Calcula a rota completa para um pagamento: lista ordenada de provedores
    /// (preferido primeiro) considerando regras configuradas, faixa de valor,
    /// moeda/pais e provedores ja tentados. O orchestrator (P0) usa apenas o
    /// primeiro; em P1 itera para fallback automatico.
    /// </summary>
    Task<RoutingPlan> PlanejarRotaAsync(RoutingContext contexto, CancellationToken ct = default);
}

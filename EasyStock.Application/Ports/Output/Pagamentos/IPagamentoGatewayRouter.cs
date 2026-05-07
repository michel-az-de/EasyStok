namespace EasyStock.Application.Ports.Output.Pagamentos;

/// <summary>
/// Resolve qual <see cref="IPagamentoGateway"/> usar dado o contexto
/// (empresa + metodo de pagamento solicitado).
///
/// <para>
/// Estrategia default: lookup em-memoria pelos gateways registrados no DI,
/// retornando o primeiro que <c>SuportaMetodo(metodo)</c>. Para multi-tenant
/// com gateway proprio (white-label), a implementacao pode consultar
/// configuracao por empresa antes do fallback global.
/// </para>
/// </summary>
public interface IPagamentoGatewayRouter
{
    /// <summary>
    /// Resolve o gateway para uma <c>(empresaId, metodo)</c>. Retorna null
    /// se nenhum gateway suportar o metodo na configuracao atual — caller
    /// deve tratar como "metodo indisponivel".
    /// </summary>
    IPagamentoGateway? Resolver(Guid empresaId, string metodo);

    /// <summary>Resolve por nome explicito do provedor — usado para webhooks.</summary>
    IPagamentoGateway? ResolverPorProvedor(string provedor);
}

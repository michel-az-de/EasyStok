using EasyStock.Application.Ports.Output.Pagamentos;

namespace EasyStock.Infra.Async.Pagamentos;

/// <summary>
/// Implementacao default do <see cref="IPagamentoGatewayRouter"/>: lookup
/// em-memoria pelos gateways registrados no DI.
///
/// <para>
/// Estrategia <c>Resolver(empresaId, metodo)</c>: percorre os gateways e
/// retorna o primeiro que <c>SuportaMetodo(metodo)</c>. Para multi-tenant
/// com gateway proprio (white-label), adicionar uma camada anterior que
/// consulta configuracao por empresa (futuro).
/// </para>
///
/// <para>
/// <c>ResolverPorProvedor(provedor)</c>: lookup direto por nome — usado
/// pelo controller de webhook para despachar payload ao processor.
/// </para>
/// </summary>
public sealed class PagamentoGatewayRouter(IEnumerable<IPagamentoGateway> gateways) : IPagamentoGatewayRouter
{
    private readonly IReadOnlyList<IPagamentoGateway> _gateways = gateways.ToList();

    public IPagamentoGateway? Resolver(Guid empresaId, string metodo)
    {
        if (string.IsNullOrWhiteSpace(metodo)) return null;
        // FUTURO: consultar configuracao por empresa antes do fallback global.
        return _gateways.FirstOrDefault(g => g.SuportaMetodo(metodo));
    }

    public IPagamentoGateway? ResolverPorProvedor(string provedor)
    {
        if (string.IsNullOrWhiteSpace(provedor)) return null;
        return _gateways.FirstOrDefault(g =>
            string.Equals(g.Provedor, provedor, StringComparison.OrdinalIgnoreCase));
    }
}

using EasyStock.Application.Ports.Output.Pagamentos;

namespace EasyStock.Infra.Async.Pagamentos;

/// <summary>
/// Implementacao do <see cref="IPagamentoGatewayRouter"/> com smart routing
/// baseado em <c>GatewayRoutingRule</c> persistida.
///
/// <para>
/// <b>Onda P0</b>:
/// </para>
/// <list type="bullet">
///   <item><see cref="Resolver"/> (compat): lookup em-memoria pelo primeiro
///   gateway registrado que <c>SuportaMetodo</c>. Mantem comportamento legado
///   para callers que ainda nao migraram (ex: webhooks).</item>
///   <item><see cref="ResolverPorProvedor"/> (compat): lookup por nome.</item>
///   <item><see cref="PlanejarRotaAsync"/> (NOVO): consulta
///   <c>IGatewayRoutingRuleRepository</c>, particiona regras de tenant vs
///   globais (tenant tem precedencia), filtra por health store
///   (<see cref="IGatewayHealthStore.PodeUsar"/>) e cross-checa com gateways
///   registrados no DI. Retorna <see cref="RoutingPlan"/> ordenado.</item>
/// </list>
///
/// <para>
/// Em P1, <see cref="PlanejarRotaAsync"/> ganha re-rank por estado de saude
/// (gateways degradados vao pro fim do bucket).
/// </para>
/// </summary>
public sealed class PagamentoGatewayRouter(
    IEnumerable<IPagamentoGateway> gateways,
    IGatewayRoutingRuleRepository ruleRepository,
    IGatewayHealthStore healthStore) : IPagamentoGatewayRouter
{
    private readonly IReadOnlyList<IPagamentoGateway> _gateways = gateways.ToList();
    private readonly IGatewayRoutingRuleRepository _ruleRepository = ruleRepository;
    private readonly IGatewayHealthStore _healthStore = healthStore;

    public IPagamentoGateway? Resolver(Guid empresaId, string metodo)
    {
        if (string.IsNullOrWhiteSpace(metodo)) return null;
        // Compat legado — orchestrator usa PlanejarRotaAsync.
        return _gateways.FirstOrDefault(g => g.SuportaMetodo(metodo));
    }

    public IPagamentoGateway? ResolverPorProvedor(string provedor)
    {
        if (string.IsNullOrWhiteSpace(provedor)) return null;
        return _gateways.FirstOrDefault(g =>
            string.Equals(g.Provedor, provedor, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<RoutingPlan> PlanejarRotaAsync(RoutingContext contexto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        if (string.IsNullOrWhiteSpace(contexto.Metodo))
            return RoutingPlan.Vazio("metodo-vazio");

        var regras = await _ruleRepository.ObterRegrasAplicaveisAsync(
            contexto.EmpresaId, contexto.Metodo, contexto.Moeda, contexto.Pais, ct);

        if (regras.Count == 0)
            return RoutingPlan.Vazio("sem-regra-aplicavel");

        // Filtra por faixa de valor (em centavos).
        var valorCentavos = (long)Math.Round(contexto.Valor * 100m, MidpointRounding.AwayFromZero);
        var regrasNaFaixa = regras.Where(r => r.AtendeFaixaValor(valorCentavos)).ToList();
        if (regrasNaFaixa.Count == 0)
            return RoutingPlan.Vazio("sem-regra-na-faixa-de-valor");

        // Tenant override: se ha pelo menos 1 regra do tenant, ignora globais.
        var tenantRules = regrasNaFaixa.Where(r => r.EmpresaId == contexto.EmpresaId).ToList();
        var globalRules = regrasNaFaixa.Where(r => r.EmpresaId == null).ToList();
        var baseRules = tenantRules.Count > 0 ? tenantRules : globalRules;

        var jaTentados = contexto.ProvedoresJaTentados ?? Array.Empty<string>();
        var skipHealth = false;

        var candidatos = baseRules
            .OrderBy(r => r.Prioridade)
            .ThenBy(r => r.Id)
            .Where(r => !jaTentados.Contains(r.Provedor, StringComparer.OrdinalIgnoreCase))
            .Where(r =>
            {
                var pode = _healthStore.PodeUsar(r.Provedor);
                if (!pode) skipHealth = true;
                return pode;
            })
            // Cross-check: gateway esta registrado no DI E suporta o metodo?
            .Where(r => _gateways.Any(g =>
                string.Equals(g.Provedor, r.Provedor, StringComparison.OrdinalIgnoreCase) &&
                g.SuportaMetodo(contexto.Metodo)))
            .ToList();

        if (candidatos.Count == 0)
        {
            var motivoVazio = jaTentados.Count > 0
                ? "todos-candidatos-ja-tentados-ou-sem-gateway"
                : "sem-gateway-registrado-para-regra";
            return RoutingPlan.Vazio(motivoVazio);
        }

        var motivo = tenantRules.Count > 0 ? "tenant-override" : "global-priority";
        if (skipHealth) motivo += "+health-skip";

        return new RoutingPlan(
            ProvedoresOrdenados: candidatos.Select(r => r.Provedor).ToList(),
            Motivo: motivo,
            RegrasAplicadasIds: candidatos.Select(r => r.Id).ToList());
    }
}

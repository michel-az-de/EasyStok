namespace EasyStock.Domain.Enums.Pagamentos;

/// <summary>
/// Estado agregado de saude de um gateway (success rate + latencia + circuit).
/// Calculado pelo <c>IGatewayHealthStore</c> (P1) sobre janela rolante de 5min.
/// Em P0 a tabela <c>gateway_health_snapshots</c> existe mas nao e populada
/// — todos gateways sao considerados <c>Saudavel</c> por default no router.
/// </summary>
public enum EstadoSaudeGateway : byte
{
    /// <summary>Operacao normal — disponivel pro routing.</summary>
    Saudavel = 1,

    /// <summary>Success rate baixo OU latencia alta — ainda usavel mas penaliza no re-rank.</summary>
    Degradado = 2,

    /// <summary>Gateway tirado do roteamento por <c>SuspensoAte</c>. Half-open canary depois.</summary>
    Suspenso = 3
}

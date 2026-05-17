namespace EasyStock.Application.Ports.Output.Pagamentos;

/// <summary>
/// Contexto para o calculo de rota — input do
/// <see cref="IPagamentoGatewayRouter.PlanejarRotaAsync"/>.
/// </summary>
public sealed record RoutingContext(
    Guid EmpresaId,
    string Metodo,
    decimal Valor,
    string Moeda = "BRL",
    string Pais = "BR",
    IReadOnlyList<string>? ProvedoresJaTentados = null,
    string? CorrelationId = null);

/// <summary>
/// Plano de roteamento — output do
/// <see cref="IPagamentoGatewayRouter.PlanejarRotaAsync"/>. Lista de provedores
/// em ordem de preferencia (primeiro = preferido para fallback). Lista vazia
/// = metodo indisponivel para esse tenant/moeda/pais.
/// </summary>
public sealed record RoutingPlan(
    IReadOnlyList<string> ProvedoresOrdenados,
    string Motivo,
    IReadOnlyList<Guid> RegrasAplicadasIds)
{
    public static RoutingPlan Vazio(string motivo = "metodo-indisponivel") =>
        new(Array.Empty<string>(), motivo, Array.Empty<Guid>());
}

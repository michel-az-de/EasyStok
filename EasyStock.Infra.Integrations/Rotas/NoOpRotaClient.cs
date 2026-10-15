using EasyStock.Application.Ports.Output.Lookup;

namespace EasyStock.Infra.Integrations.Rotas;

/// <summary>
/// Sem provedor de rota configurado: não bate na rede e devolve
/// <see langword="null"/>, então o frete segue com <c>haversine × FatorRota</c>.
/// </summary>
public sealed class NoOpRotaClient : IRotaClient
{
    public Task<RotaResultado?> MedirAsync(RotaQuery query, CancellationToken ct = default) =>
        Task.FromResult<RotaResultado?>(null);
}

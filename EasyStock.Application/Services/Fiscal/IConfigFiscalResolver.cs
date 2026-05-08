using EasyStock.Application.Ports.Output.Fiscal;

namespace EasyStock.Application.Services.Fiscal;

/// <summary>
/// Resolve a ConfigFiscalDto necessária pelo gateway a partir de Empresa,
/// Loja e (futuramente) configurações do tenant. Centraliza a leitura
/// para que use cases não precisem lidar com fallbacks.
/// </summary>
public interface IConfigFiscalResolver
{
    Task<ConfigFiscalDto> ResolverAsync(Guid empresaId, Guid lojaId, CancellationToken ct);
}

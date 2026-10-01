namespace EasyStock.Application.Ports.Output.Persistence;

/// <summary>
/// Empresas que o vigia das integrações acompanha (F16, #1246): as que têm o módulo de
/// atendimento ligado ou alguma chave de integração ativa. Cross-tenant: o host liga o bypass de
/// RLS antes de chamar.
/// </summary>
public interface IAlvosVigiaIntegracoesQuery
{
    Task<IReadOnlyList<Guid>> ListarEmpresasAsync(CancellationToken ct = default);
}

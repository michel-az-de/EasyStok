using System.Collections.Concurrent;
using EasyStock.Application.Ports.Output.Integration.Conexao;

namespace EasyStock.Application.UseCases.Integracoes;

/// <summary>
/// Último teste das chaves globais, na memória do processo (singleton). Ver
/// <see cref="IEstadoTesteIntegracaoStore"/>: a API roda numa réplica só e o vigia repõe o estado
/// em até 15 min depois de um restart.
/// </summary>
public sealed class EstadoTesteIntegracaoEmMemoria : IEstadoTesteIntegracaoStore
{
    private readonly ConcurrentDictionary<(Guid, string), EstadoTesteIntegracao> _estados = new();

    public EstadoTesteIntegracao? Obter(Guid empresaId, string provider) =>
        _estados.TryGetValue((empresaId, provider), out var estado) ? estado : null;

    public void Registrar(Guid empresaId, string provider, EstadoTesteIntegracao estado) =>
        _estados[(empresaId, provider)] = estado;
}

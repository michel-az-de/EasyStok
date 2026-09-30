using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Ports.Output.Persistence.Atendimento;

/// <summary>Biblioteca de respostas prontas do console (S42). Toda consulta leva <c>EmpresaId</c> no WHERE (ADR-0010).</summary>
public interface IRespostaProntaRepository
{
    Task AddAsync(RespostaPronta resposta, CancellationToken ct = default);

    /// <summary>Rastreado.</summary>
    Task<RespostaPronta?> ObterAsync(Guid empresaId, Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<RespostaPronta>> ListarAsync(Guid empresaId, bool incluirArquivadas, CancellationToken ct = default);

    /// <summary>Atalho (já normalizado) em uso por outra resposta da empresa, arquivada ou não.</summary>
    Task<bool> ExisteAtalhoAsync(Guid empresaId, string atalho, Guid? excetoId, CancellationToken ct = default);
}

/// <summary>Mensagens automáticas por gatilho (S42): uma regra por gatilho e empresa.</summary>
public interface IRegraAutomaticaRepository
{
    Task AddAsync(RegraAutomatica regra, CancellationToken ct = default);

    /// <summary>Rastreado.</summary>
    Task<RegraAutomatica?> ObterPorGatilhoAsync(Guid empresaId, GatilhoAutomacao gatilho, CancellationToken ct = default);

    Task<IReadOnlyList<RegraAutomatica>> ListarAsync(Guid empresaId, CancellationToken ct = default);
}

using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Application.Ports.Output.Persistence.Atendimento;

/// <summary>Mensagens padrão locais (#3204). Toda consulta leva <c>EmpresaId</c> no WHERE (ADR-0010).</summary>
public interface IModeloMensagemLocalRepository
{
    Task AddAsync(ModeloMensagemLocal modelo, CancellationToken ct = default);

    /// <summary>Rastreado.</summary>
    Task<ModeloMensagemLocal?> ObterAsync(Guid empresaId, Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<ModeloMensagemLocal>> ListarAsync(Guid empresaId, bool incluirArquivados, CancellationToken ct = default);

    /// <summary>Nome e idioma (já normalizados) em uso por outro modelo da empresa, arquivado ou não.</summary>
    Task<bool> ExisteAsync(Guid empresaId, string nome, string idioma, Guid? excetoId, CancellationToken ct = default);
}

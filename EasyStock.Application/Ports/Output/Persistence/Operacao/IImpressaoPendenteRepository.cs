using EasyStock.Domain.Entities.Operacao;

namespace EasyStock.Application.Ports.Output.Persistence.Operacao;

/// <summary>Impressão pendente há tempo demais, vista pelo job de alerta (cross-tenant).</summary>
public sealed record ImpressaoAtrasada(Guid ImpressaoId, Guid EmpresaId, Guid PedidoId, DateTime CriadaEm);

/// <summary>
/// Fila de impressão (S20). <c>empresaId</c> no WHERE das consultas de tenant além do filtro global e do
/// RLS (ADR-0010). <see cref="ListarAtrasadasAsync"/> é a única cross-tenant (job sem tenant).
/// </summary>
public interface IImpressaoPendenteRepository
{
    Task AddAsync(ImpressaoPendente impressao, CancellationToken ct = default);

    /// <summary>Carrega com tracking para o use case mudar o status e comitar.</summary>
    Task<ImpressaoPendente?> GetByIdAsync(Guid empresaId, Guid id, CancellationToken ct = default);

    /// <summary>As pendentes da empresa, mais antigas primeiro (ordem de impressão).</summary>
    Task<IReadOnlyList<ImpressaoPendente>> ListarPendentesAsync(Guid empresaId, int limite, CancellationToken ct = default);

    /// <summary>Pendentes criadas entre <paramref name="criadasDepoisDe"/> e <paramref name="criadasAntesDe"/>, de todas as empresas.</summary>
    Task<IReadOnlyList<ImpressaoAtrasada>> ListarAtrasadasAsync(
        DateTime criadasDepoisDe, DateTime criadasAntesDe, int maximo, CancellationToken ct = default);
}

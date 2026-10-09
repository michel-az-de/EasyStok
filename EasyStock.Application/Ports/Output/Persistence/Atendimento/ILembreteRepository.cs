using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Ports.Output.Persistence.Atendimento;

/// <summary>
/// Lembretes da dona (S43). As consultas do console levam <c>EmpresaId</c> no WHERE (ADR-0010). As
/// marcadas "cross-tenant" são do avaliador, que roda com bypass de RLS ligado pelo host.
/// </summary>
public interface ILembreteRepository
{
    Task AddAsync(Lembrete lembrete, CancellationToken ct = default);

    /// <summary>Rastreado.</summary>
    Task<Lembrete?> ObterAsync(Guid empresaId, Guid id, CancellationToken ct = default);

    /// <summary>
    /// Sininho: os lembretes para <paramref name="usuarioId"/> e os da equipe toda (sem destinatário).
    /// <paramref name="usuarioId"/> nulo traz todos da empresa. Somente leitura.
    /// </summary>
    Task<IReadOnlyList<Lembrete>> ListarAsync(
        Guid empresaId, Guid? usuarioId, bool incluirConcluidos, int limite, CancellationToken ct = default);

    /// <summary>Persiste os vistos em uma operação: abertos, vencidos, ainda não vistos e visíveis ao usuário. Devolve quantos mudaram.</summary>
    Task<int> MarcarVencidosVistosAsync(Guid empresaId, Guid usuarioId, DateTime agoraUtc, CancellationToken ct = default);

    /// <summary>Idempotência do avaliador: já existe lembrete (em qualquer situação) para o fato?</summary>
    Task<bool> ExisteAutomaticoAsync(Guid empresaId, TipoLembrete tipo, string referencia, CancellationToken ct = default);

    /// <summary>Cross-tenant: automáticos abertos, para o avaliador resolver os que deixaram de valer. Rastreado.</summary>
    Task<IReadOnlyList<Lembrete>> ListarAutomaticosAbertosAsync(CancellationToken ct = default);

    /// <summary>Cross-tenant: abertos que venceram e ainda não foram avisados. Rastreado.</summary>
    Task<IReadOnlyList<Lembrete>> ListarVencidosSemAvisoAsync(DateTime agoraUtc, int limite, CancellationToken ct = default);
}

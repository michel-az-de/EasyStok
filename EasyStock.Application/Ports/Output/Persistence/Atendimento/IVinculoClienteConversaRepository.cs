using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Application.Ports.Output.Persistence.Atendimento;

public sealed record PedidoDoVinculo(Guid Id, string Status, decimal Total);
public sealed record CandidatoClienteConversa(Guid Id, string Nome, string? Telefone, bool EhLead, bool Bloqueado,
    int QuantidadePedidos, DateTime? UltimoPedidoEm, bool Ativo);

public interface IVinculoClienteConversaRepository
{
    Task<IReadOnlyList<PedidoDoVinculo>> ListarPedidosAsync(Guid empresaId, Guid conversaId, CancellationToken ct);
    Task<IReadOnlyList<CandidatoClienteConversa>> ListarCandidatosAsync(Guid empresaId, IReadOnlyList<string> telefones, IReadOnlyList<string> hashes, CancellationToken ct);
    Task<IReadOnlyList<AlteracaoVinculoConversa>> ListarHistoricoAsync(Guid empresaId, Guid conversaId, CancellationToken ct);
    Task<AlteracaoVinculoConversa?> ObterTentativaAsync(Guid empresaId, Guid conversaId, Guid requestId, CancellationToken ct);
    Task AddAsync(AlteracaoVinculoConversa alteracao, CancellationToken ct);
    Task ExpirarLinksAsync(Guid empresaId, Guid conversaId, DateTime agora, CancellationToken ct);
    Task EncerrarSessoesChatAsync(Guid empresaId, Guid conversaId, DateTime agora, CancellationToken ct);
}

using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Application.Ports.Output.Persistence.Atendimento;

/// <summary>Consentimentos do cliente final por canal e finalidade (S38). <c>EmpresaId</c> no WHERE (ADR-0010).</summary>
public interface IConsentimentoContatoRepository
{
    /// <summary>Rastreado: quem altera chama <see cref="IUnitOfWork.CommitAsync"/> em seguida.</summary>
    Task<IReadOnlyList<ConsentimentoContato>> ListarDoClienteAsync(Guid empresaId, Guid clienteId, CancellationToken ct = default);

    Task AddAsync(ConsentimentoContato consentimento, CancellationToken ct = default);
}

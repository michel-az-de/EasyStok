using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories.Atendimento;

public sealed class ConsentimentoContatoRepository(EasyStockDbContext db) : IConsentimentoContatoRepository
{
    public async Task<IReadOnlyList<ConsentimentoContato>> ListarDoClienteAsync(Guid empresaId, Guid clienteId, CancellationToken ct = default) =>
        await db.ConsentimentosContato
            .Where(c => c.EmpresaId == empresaId && c.ClienteId == clienteId)
            .ToListAsync(ct);

    public Task AddAsync(ConsentimentoContato consentimento, CancellationToken ct = default) =>
        db.ConsentimentosContato.AddAsync(consentimento, ct).AsTask();
}

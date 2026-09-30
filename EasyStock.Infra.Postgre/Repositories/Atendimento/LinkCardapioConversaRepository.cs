using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories.Atendimento;

public sealed class LinkCardapioConversaRepository(EasyStockDbContext db) : ILinkCardapioConversaRepository
{
    public Task AddAsync(LinkCardapioConversa link, CancellationToken ct = default) =>
        db.LinksCardapioConversa.AddAsync(link, ct).AsTask();

    public async Task<LinkCardapioConversa?> ObterPorTokenHashAsync(string tokenHash, CancellationToken ct = default)
    {
        // Cross-tenant: a requisição do site é anônima e o token não diz a loja. O hash de 256 bits
        // é a credencial; o caller liga o tenant do link encontrado antes de qualquer outra query.
        using var _ = db.UseRowLevelSecurityBypass();
        return await db.LinksCardapioConversa
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.TokenHash == tokenHash, ct);
    }

    public async Task<bool> TentarConsumirAsync(Guid empresaId, Guid linkId, DateTime agora, CancellationToken ct = default) =>
        await db.LinksCardapioConversa
            .Where(l => l.EmpresaId == empresaId && l.Id == linkId && l.UsadoEm == null && l.ExpiraEm > agora)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.UsadoEm, agora), ct) == 1;

    public Task LiberarAsync(Guid empresaId, Guid linkId, CancellationToken ct = default) =>
        db.LinksCardapioConversa
            .Where(l => l.EmpresaId == empresaId && l.Id == linkId)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.UsadoEm, (DateTime?)null), ct);
}

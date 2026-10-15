using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Enums.Pagamentos;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories.Pagamentos;

/// <summary>
/// Persistência de <see cref="CobrancaPedido"/> (S11). <c>empresaId</c> no WHERE das consultas de tenant
/// além do filtro global e do RLS (ADR-0010). As duas consultas sem tenant (webhook sem JWT e varredura
/// do job) são cross-tenant por natureza: <c>IgnoreQueryFilters</c> + bypass de RLS em escopo curto, e
/// devolvem só ids para o chamador ligar o tenant antes de tocar no pedido.
/// </summary>
public sealed class CobrancaPedidoRepository(EasyStockDbContext db) : ICobrancaPedidoRepository
{
    public async Task AddAsync(CobrancaPedido cobranca, CancellationToken ct = default) =>
        await db.CobrancasPedido.AddAsync(cobranca, ct);

    public async Task<IReadOnlyList<CobrancaPedido>> ListarDoPedidoAsync(Guid empresaId, Guid pedidoId, CancellationToken ct = default) =>
        await db.CobrancasPedido
            .Where(c => c.EmpresaId == empresaId && c.PedidoId == pedidoId)
            .OrderBy(c => c.CriadaEm)
            .ThenBy(c => c.Id)
            .ToListAsync(ct);

    public async Task<Guid?> ObterEmpresaIdDoPedidoAsync(Guid pedidoId, CancellationToken ct = default)
    {
        using var _ = db.UseRowLevelSecurityBypass();
        return await db.CobrancasPedido
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.PedidoId == pedidoId)
            .Select(c => (Guid?)c.EmpresaId)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<CobrancaPedidoVencida>> ListarPendentesVencidasAsync(
        DateTime limite, int maximo, CancellationToken ct = default)
    {
        using var _ = db.UseRowLevelSecurityBypass();
        return await db.CobrancasPedido
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.Status == StatusCobrancaPedido.Pendente
                        && c.Provedor == CobrancaPedido.ProvedorMercadoPago
                        && c.ExpiraEm <= limite
                        // I3.2: quem aguarda credencial fica fora, senão as mesmas presas ocupam a rodada.
                        && c.AguardandoCredencialDesde == null)
            .OrderBy(c => c.ExpiraEm)
            .Take(Math.Clamp(maximo, 1, 500))
            .Select(c => new CobrancaPedidoVencida(c.Id, c.EmpresaId, c.PedidoId, c.ContaRecebedoraId))
            .ToListAsync(ct);
    }

    public async Task<bool> ExisteAguardandoCredencialAsync(CancellationToken ct = default)
    {
        using var _ = db.UseRowLevelSecurityBypass();
        return await db.CobrancasPedido
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(c => c.Status == StatusCobrancaPedido.Pendente && c.AguardandoCredencialDesde != null, ct);
    }

    public async Task<int> RetomarAguardandoCredencialAsync(string contaRecebedoraId, CancellationToken ct = default)
    {
        using var _ = db.UseRowLevelSecurityBypass();
        return await db.CobrancasPedido
            .IgnoreQueryFilters()
            .Where(c => c.Status == StatusCobrancaPedido.Pendente
                        && c.AguardandoCredencialDesde != null
                        && (c.ContaRecebedoraId == null || c.ContaRecebedoraId == contaRecebedoraId))
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.AguardandoCredencialDesde, (DateTime?)null), ct);
    }

    public async Task<bool> ExistePendenteOnlineAsync(CancellationToken ct = default)
    {
        using var _ = db.UseRowLevelSecurityBypass();
        return await db.CobrancasPedido
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(c => c.Status == StatusCobrancaPedido.Pendente
                           && c.Provedor == CobrancaPedido.ProvedorMercadoPago, ct);
    }
}

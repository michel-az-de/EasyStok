using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Enums.Pagamentos;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories.Pagamentos;

public sealed class EstornoOnlineRepository(EasyStockDbContext db) : IEstornoOnlineRepository
{
    public async Task<IReadOnlyList<RecebimentoOnline>> RecebimentosAsync(Guid empresaId, Guid pedidoId, CancellationToken ct) =>
        await (from p in db.Pedidos.AsNoTracking()
               from r in p.Pagamentos
               join c in db.CobrancasPedido.AsNoTracking() on new { PedidoId = p.Id, Referencia = r.Referencia } equals
                   new { c.PedidoId, Referencia = c.PagamentoExternoId }
               where p.EmpresaId == empresaId && p.Id == pedidoId && c.EmpresaId == empresaId
                   && c.Provedor == CobrancaPedido.ProvedorMercadoPago
                   && (c.Status == StatusCobrancaPedido.Paga || c.Status == StatusCobrancaPedido.Estornada)
               select new RecebimentoOnline(r.Id, c.Id, c.PagamentoExternoId!, r.Valor, r.Metodo, r.PagoEm,
                   p.LojaId, c.Status == StatusCobrancaPedido.Estornada)).ToListAsync(ct);

    public async Task<IReadOnlyList<PedidoEstornoOnline>> ListarAsync(Guid empresaId, Guid pedidoId, CancellationToken ct) =>
        await db.Set<PedidoEstornoOnline>().AsNoTracking()
            .Where(e => e.EmpresaId == empresaId && e.PedidoId == pedidoId).OrderBy(e => e.CriadoEm).ThenBy(e => e.Id).ToListAsync(ct);

    public async Task AdicionarAsync(PedidoEstornoOnline estorno, CancellationToken ct) =>
        await db.Set<PedidoEstornoOnline>().AddAsync(estorno, ct);

    public Task AtualizarAsync(PedidoEstornoOnline estorno, CancellationToken ct)
    {
        // Cada fase relê o banco depois do lock. Não reaproveitar o estado anterior à chamada HTTP.
        var anterior = db.Set<PedidoEstornoOnline>().Local.FirstOrDefault(e => e.Id == estorno.Id);
        if (anterior is not null) db.Entry(anterior).State = EntityState.Detached;
        db.Update(estorno);
        return Task.CompletedTask;
    }

    public async Task MarcarCobrancaEstornadaAsync(Guid empresaId, Guid cobrancaId, DateTime agora, CancellationToken ct)
    {
        var cobranca = await db.CobrancasPedido.SingleAsync(c => c.EmpresaId == empresaId && c.Id == cobrancaId, ct);
        await db.Entry(cobranca).ReloadAsync(ct);
        cobranca.MarcarEstornada("Devolução integral confirmada no Mercado Pago", agora);
    }
}

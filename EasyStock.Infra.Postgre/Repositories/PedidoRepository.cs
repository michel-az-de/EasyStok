using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Sales;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories
{
    public sealed class PedidoRepository(EasyStockDbContext db) : IPedidoRepository
    {
        public Task<Pedido?> GetByIdAsync(Guid empresaId, Guid id) =>
            db.Pedidos.FirstOrDefaultAsync(p => p.EmpresaId == empresaId && p.Id == id);

        public Task<Pedido?> GetByIdWithDetailsAsync(Guid empresaId, Guid id) =>
            db.Pedidos
                .Include(p => p.Itens)
                .Include(p => p.Pagamentos)
                .FirstOrDefaultAsync(p => p.EmpresaId == empresaId && p.Id == id);

        public Task<Pedido?> FindByMobileOrderIdAsync(Guid empresaId, string mobileOrderId) =>
            db.Pedidos.FirstOrDefaultAsync(p =>
                p.EmpresaId == empresaId && p.MobileOrderId == mobileOrderId);

        public async Task<(IEnumerable<Pedido> items, int total)> GetByEmpresaAsync(
            Guid empresaId, int page, int pageSize,
            string? status = null, Guid? clienteId = null,
            DateTime? desde = null, DateTime? ate = null,
            string? search = null, string? sort = "criadoem", string? order = "desc")
        {
            var query = db.Pedidos.AsNoTracking()
                .Where(p => p.EmpresaId == empresaId);

            if (!string.IsNullOrWhiteSpace(status)) query = query.Where(p => p.Status == status);
            if (clienteId.HasValue && clienteId.Value != Guid.Empty)
                query = query.Where(p => p.ClienteId == clienteId);
            if (desde.HasValue) query = query.Where(p => p.CriadoEm >= desde.Value);
            if (ate.HasValue)   query = query.Where(p => p.CriadoEm <= ate.Value);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var termo = search.Trim();
                query = query.Where(p =>
                    (p.ClienteNome != null && EF.Functions.ILike(p.ClienteNome, $"%{termo}%")) ||
                    (p.ClienteApt  != null && EF.Functions.ILike(p.ClienteApt,  $"%{termo}%")) ||
                    (p.ClienteTelefone != null && EF.Functions.ILike(p.ClienteTelefone, $"%{termo}%")) ||
                    (p.Observacoes != null && EF.Functions.ILike(p.Observacoes, $"%{termo}%")));
            }

            var total = await query.CountAsync();
            var desc = string.Equals(order, "desc", StringComparison.OrdinalIgnoreCase);

            query = sort?.ToLowerInvariant() switch
            {
                "total"   => desc ? query.OrderByDescending(p => p.Total)     : query.OrderBy(p => p.Total),
                "status"  => desc ? query.OrderByDescending(p => p.Status)    : query.OrderBy(p => p.Status),
                "cliente" => desc ? query.OrderByDescending(p => p.ClienteNome) : query.OrderBy(p => p.ClienteNome),
                _         => desc ? query.OrderByDescending(p => p.CriadoEm)  : query.OrderBy(p => p.CriadoEm),
            };

            var pedidos = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            return (pedidos, total);
        }

        public async Task<IEnumerable<Pedido>> ListByClienteAsync(Guid empresaId, Guid clienteId, int max = 50) =>
            await db.Pedidos.AsNoTracking()
                .Where(p => p.EmpresaId == empresaId && p.ClienteId == clienteId)
                .OrderByDescending(p => p.CriadoEm)
                .Take(max)
                .ToListAsync();

        public Task AddAsync(Pedido pedido) { db.Pedidos.Add(pedido); return Task.CompletedTask; }
        public Task UpdateAsync(Pedido pedido) { db.Pedidos.Update(pedido); return Task.CompletedTask; }

        // ── Sub-recursos ──────────────────────────────────────────
        public Task AddItemAsync(PedidoItem item) { db.Set<PedidoItem>().Add(item); return Task.CompletedTask; }
        public Task RemoveItemAsync(Guid itemId) =>
            db.Set<PedidoItem>().Where(i => i.Id == itemId).ExecuteDeleteAsync();

        public Task AddEventoAsync(PedidoEvento evento) { db.Set<PedidoEvento>().Add(evento); return Task.CompletedTask; }

        public async Task<IEnumerable<PedidoEvento>> GetEventosAsync(Guid pedidoId, int max = 200) =>
            await db.Set<PedidoEvento>().AsNoTracking()
                .Where(e => e.PedidoId == pedidoId)
                .OrderByDescending(e => e.OcorridoEm)
                .Take(max).ToListAsync();

        public Task AddPagamentoAsync(PedidoPagamento pagamento) { db.Set<PedidoPagamento>().Add(pagamento); return Task.CompletedTask; }
        public Task RemovePagamentoAsync(Guid pagamentoId) =>
            db.Set<PedidoPagamento>().Where(p => p.Id == pagamentoId).ExecuteDeleteAsync();

        // Strings canônicas dos status "abertos" derivadas da PedidoStateMachine.
        // Materializa em ToArray() pra que o EF Core traduza em IN (...) no SQL.
        private static readonly string[] StatusAbertos = PedidoStateMachine.Abertos
            .Select(StatusPedidoMapper.Format)
            .ToArray();

        public Task<bool> ExistemPedidosAbertosComProdutoAsync(Guid empresaId, Guid produtoId)
        {
            return db.Pedidos.AsNoTracking()
                .Where(p => p.EmpresaId == empresaId && StatusAbertos.Contains(p.Status))
                .SelectMany(p => p.Itens)
                .AnyAsync(i => i.ProdutoId == produtoId);
        }
    }
}

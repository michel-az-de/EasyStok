using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories
{
    public sealed class CaixaRepository(EasyStockDbContext db) : ICaixaRepository
    {
        public Task<MovimentoCaixa?> GetMovimentoAsync(Guid empresaId, Guid id) =>
            db.MovimentosCaixa.FirstOrDefaultAsync(m => m.EmpresaId == empresaId && m.Id == id);

        public async Task<(IEnumerable<MovimentoCaixa> items, int total)> ListMovimentosAsync(
            Guid empresaId, int page, int pageSize,
            string? tipo = null, DateTime? desde = null, DateTime? ate = null,
            bool incluirEstornados = false,
            string? sort = "datamovimento", string? order = "desc")
        {
            var query = db.MovimentosCaixa.AsNoTracking()
                .Where(m => m.EmpresaId == empresaId);

            if (!incluirEstornados) query = query.Where(m => m.EstornadoEm == null);
            if (!string.IsNullOrWhiteSpace(tipo)) query = query.Where(m => m.Tipo == tipo);
            if (desde.HasValue) query = query.Where(m => m.DataMovimento >= desde.Value);
            if (ate.HasValue)   query = query.Where(m => m.DataMovimento <= ate.Value);

            var total = await query.CountAsync();
            var desc = string.Equals(order, "desc", StringComparison.OrdinalIgnoreCase);

            query = sort?.ToLowerInvariant() switch
            {
                "valor" => desc ? query.OrderByDescending(m => m.Valor) : query.OrderBy(m => m.Valor),
                "tipo"  => desc ? query.OrderByDescending(m => m.Tipo)  : query.OrderBy(m => m.Tipo),
                _       => desc ? query.OrderByDescending(m => m.DataMovimento) : query.OrderBy(m => m.DataMovimento),
            };

            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            return (items, total);
        }

        public async Task<IEnumerable<MovimentoCaixa>> GetMovimentosDoDiaAsync(Guid empresaId, DateOnly data, Guid? lojaId = null)
        {
            var inicio = data.ToDateTime(TimeOnly.MinValue);
            var fim    = data.AddDays(1).ToDateTime(TimeOnly.MinValue);

            var q = db.MovimentosCaixa.AsNoTracking()
                .Where(m => m.EmpresaId == empresaId && m.EstornadoEm == null
                         && m.DataMovimento >= inicio && m.DataMovimento < fim);
            if (lojaId.HasValue) q = q.Where(m => m.LojaId == lojaId);
            return await q.OrderBy(m => m.DataMovimento).ToListAsync();
        }

        public Task AddMovimentoAsync(MovimentoCaixa m) { db.MovimentosCaixa.Add(m); return Task.CompletedTask; }
        public Task UpdateMovimentoAsync(MovimentoCaixa m) { db.MovimentosCaixa.Update(m); return Task.CompletedTask; }

        public Task<FechamentoCaixa?> GetFechamentoDoDiaAsync(Guid empresaId, DateOnly data, Guid? lojaId = null) =>
            db.FechamentosCaixa.FirstOrDefaultAsync(f =>
                f.EmpresaId == empresaId && f.Data == data && f.LojaId == lojaId);

        public async Task<(IEnumerable<FechamentoCaixa> items, int total)> ListFechamentosAsync(
            Guid empresaId, int page, int pageSize, DateOnly? desde = null, DateOnly? ate = null)
        {
            var q = db.FechamentosCaixa.AsNoTracking().Where(f => f.EmpresaId == empresaId);
            if (desde.HasValue) q = q.Where(f => f.Data >= desde.Value);
            if (ate.HasValue)   q = q.Where(f => f.Data <= ate.Value);

            var total = await q.CountAsync();
            var items = await q.OrderByDescending(f => f.Data)
                .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            return (items, total);
        }

        public Task AddFechamentoAsync(FechamentoCaixa f) { db.FechamentosCaixa.Add(f); return Task.CompletedTask; }

        public async Task<decimal> GetTotalVendasDoDiaAsync(Guid empresaId, DateOnly data, Guid? lojaId = null)
        {
            var inicio = data.ToDateTime(TimeOnly.MinValue);
            var fim    = data.AddDays(1).ToDateTime(TimeOnly.MinValue);

            var q = db.Vendas.AsNoTracking()
                .Where(v => v.EmpresaId == empresaId && v.DataVenda >= inicio && v.DataVenda < fim);
            if (lojaId.HasValue) q = q.Where(v => v.LojaId == lojaId);

            var vendas = await q.ToListAsync();
            return vendas.Sum(v => v.ValorTotal == null ? 0m : v.ValorTotal.Valor);
        }

        public async Task<decimal> GetTotalPagamentosPedidosDoDiaAsync(Guid empresaId, DateOnly data, Guid? lojaId = null)
        {
            var inicio = data.ToDateTime(TimeOnly.MinValue);
            var fim    = data.AddDays(1).ToDateTime(TimeOnly.MinValue);

            var pagamentos = await db.Set<PedidoPagamento>().AsNoTracking()
                .Where(pg => pg.PagoEm >= inicio && pg.PagoEm < fim)
                .Join(db.Pedidos.AsNoTracking(),
                      pg => pg.PedidoId,
                      p => p.Id,
                      (pg, p) => new { pg, p })
                .Where(x => x.p.EmpresaId == empresaId
                         && x.p.Status != "cancelado"
                         && (lojaId == null || x.p.LojaId == lojaId))
                .Select(x => x.pg.Valor)
                .ToListAsync();

            return pagamentos.Sum();
        }
    }
}

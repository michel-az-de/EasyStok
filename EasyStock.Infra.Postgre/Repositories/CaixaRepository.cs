using EasyStock.Application.Common;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;

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

        // DataMovimento/DataVenda/PagoEm sao colunas de INSTANTE real (gravadas de UtcNow).
        // Agrupar pelo dia civil de Brasilia via JanelaDiaUtc (meia-noite BRT = 03:00Z),
        // alinhado com AbrirCaixaUseCase, HorarioBrasil.DataOperacional e o indice unico de
        // abertura (#379). Antes ancorava em 00:00Z (ToUtc), perdendo a abertura feita na
        // janela 21h-23h59 BRT (cujo timestamp UTC ja virou o dia seguinte): a tela mostrava
        // "aguardando abertura" e a reabertura batia no indice unico -> "ja existe".

        public Task<IEnumerable<MovimentoCaixa>> GetMovimentosDoDiaAsync(Guid empresaId, DateOnly data, Guid? lojaId = null)
        {
            var (inicio, fim) = HorarioBrasil.JanelaDiaUtc(data);
            return GetMovimentosNoIntervaloAsync(empresaId, inicio, fim, lojaId);
        }

        public async Task<IEnumerable<MovimentoCaixa>> GetMovimentosNoIntervaloAsync(Guid empresaId, DateTime iniUtc, DateTime fimUtc, Guid? lojaId = null)
        {
            var q = db.MovimentosCaixa.AsNoTracking()
                .Where(m => m.EmpresaId == empresaId && m.EstornadoEm == null
                         && m.DataMovimento >= iniUtc && m.DataMovimento < fimUtc);
            if (lojaId.HasValue) q = q.Where(m => m.LojaId == lojaId);
            return await q.OrderBy(m => m.DataMovimento).ToListAsync();
        }

        // Ultima abertura sem fechamento posterior (sessao em aberto, possivelmente cross-day).
        // Espelha o "ultimo evento abertura/fechamento" do AnalyticsRepository.ResumoDia (issue 596):
        // o estado da sessao e dado pelo evento mais recente; se for abertura, ha caixa em aberto.
        public async Task<MovimentoCaixa?> GetAberturaPendenteAsync(Guid empresaId, Guid? lojaId = null)
        {
            var q = db.MovimentosCaixa.AsNoTracking()
                .Where(m => m.EmpresaId == empresaId && m.EstornadoEm == null
                         && (m.Tipo == "abertura" || m.Tipo == "fechamento"));
            if (lojaId.HasValue) q = q.Where(m => m.LojaId == lojaId);
            var ultimo = await q.OrderByDescending(m => m.DataMovimento).FirstOrDefaultAsync();
            return ultimo?.Tipo == "abertura" ? ultimo : null;
        }

        // Aberturas (não estornadas) sem fechamento posterior na MESMA empresa/loja = sessão em
        // aberto. Cross-tenant: o caller (CaixaEsquecidoJob) liga UseRowLevelSecurityBypass() ANTES
        // de abrir a conexão (camada RLS) e este método desliga o filtro EF com IgnoreQueryFilters
        // (camada EF) — defesa em profundidade (ver CaixaEsquecidoCrossTenantRlsTests). Pré-filtra
        // no SQL por instante < limite (00:00 BRT de hoje em UTC) via anti-join EXISTS (traduz no
        // Npgsql, ao contrário de GroupBy().First()), e refina o dia operacional BRT em memória — a
        // conversão de fuso vive em HorarioBrasil e não traduz pra SQL. Agrupa por (empresa, loja).
        public async Task<IReadOnlyList<MovimentoCaixa>> GetAberturasEsquecidasAsync(
            DateTime limiteInferiorUtc, CancellationToken ct = default)
        {
            var candidatas = await db.MovimentosCaixa.AsNoTracking().IgnoreQueryFilters()
                .Where(a => a.Tipo == "abertura" && a.EstornadoEm == null
                         && a.DataMovimento < limiteInferiorUtc
                         && !db.MovimentosCaixa.Any(f =>
                                f.Tipo == "fechamento" && f.EstornadoEm == null
                                && f.EmpresaId == a.EmpresaId && f.LojaId == a.LojaId
                                && f.DataMovimento > a.DataMovimento))
                .ToListAsync(ct);

            var hoje = HorarioBrasil.Hoje();
            return candidatas
                .Where(a => HorarioBrasil.DataOperacional(a.DataMovimento) < hoje)
                .ToList();
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

        public Task<decimal> GetTotalVendasDoDiaAsync(Guid empresaId, DateOnly data, Guid? lojaId = null)
        {
            var (inicio, fim) = HorarioBrasil.JanelaDiaUtc(data);
            return GetTotalVendasNoIntervaloAsync(empresaId, inicio, fim, lojaId);
        }

        public async Task<decimal> GetTotalVendasNoIntervaloAsync(Guid empresaId, DateTime iniUtc, DateTime fimUtc, Guid? lojaId = null)
        {
            var q = db.Vendas.AsNoTracking()
                .Where(v => v.EmpresaId == empresaId && v.DataVenda >= iniUtc && v.DataVenda < fimUtc);
            if (lojaId.HasValue) q = q.Where(v => v.LojaId == lojaId);

            var vendas = await q.ToListAsync();
            return vendas.Sum(v => v.ValorTotal == null ? 0m : v.ValorTotal.Valor);
        }

        public Task<decimal> GetTotalPagamentosPedidosDoDiaAsync(Guid empresaId, DateOnly data, Guid? lojaId = null)
        {
            var (inicio, fim) = HorarioBrasil.JanelaDiaUtc(data);
            return GetTotalPagamentosPedidosNoIntervaloAsync(empresaId, inicio, fim, lojaId);
        }

        public async Task<decimal> GetTotalPagamentosPedidosNoIntervaloAsync(Guid empresaId, DateTime iniUtc, DateTime fimUtc, Guid? lojaId = null)
        {
            var pagamentos = await db.Set<PedidoPagamento>().AsNoTracking()
                .Where(pg => pg.PagoEm >= iniUtc && pg.PagoEm < fimUtc)
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

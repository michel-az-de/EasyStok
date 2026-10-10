using EasyStock.Application.Common;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Enums.Pagamentos;

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

        public async Task MarcarNotificadoEsquecidoAsync(Guid movimentoId, DateTime em, CancellationToken ct = default) =>
            await db.MovimentosCaixa.IgnoreQueryFilters()
                .Where(m => m.Id == movimentoId)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.NotificadoEsquecidoEm, em), ct);

        // Multi-tenant via UsuarioEmpresa; cross-tenant, então IgnoreQueryFilters (o job roda sob bypass de RLS).
        public async Task<Guid?> ResolverResponsavelPadraoAsync(Guid empresaId, CancellationToken ct = default) =>
            await db.Set<UsuarioEmpresa>().IgnoreQueryFilters()
                .Where(ue => ue.EmpresaId == empresaId && ue.Ativo)
                .OrderBy(ue => ue.CriadoEm)
                .Select(ue => (Guid?)ue.UsuarioId)
                .FirstOrDefaultAsync(ct);

        public Task AddMovimentoAsync(MovimentoCaixa m) { db.MovimentosCaixa.Add(m); return Task.CompletedTask; }
        public Task UpdateMovimentoAsync(MovimentoCaixa m) { db.MovimentosCaixa.Update(m); return Task.CompletedTask; }

        // issue 951: flush isolado da abertura automatica, dentro da tx explicita aberta pelo
        // caller (RegistrarPagamentoPedidoUseCase). Se colidir com a unique parcial por dia/loja
        // (corrida perdida), o EF Core reverte SO este SaveChanges via savepoint automatico —
        // o pagamento, ja commitado num flush anterior na MESMA tx, permanece de pe. Mesmo
        // padrao de VagaOcupadaRepository para uq_vaga_ativa_por_pedido: constraint unica simples
        // nao precisa de advisory lock, o proprio indice serializa.
        public async Task<bool> TryAddMovimentoAsync(MovimentoCaixa m, CancellationToken ct = default)
        {
            db.MovimentosCaixa.Add(m);
            try
            {
                await db.SaveChangesAsync(ct);
                return true;
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex, "ix_movimentos_caixa_abertura_unica"))
            {
                db.Entry(m).State = EntityState.Detached;
                return false;
            }
        }

        private static bool IsUniqueViolation(DbUpdateException ex, string constraintName)
        {
            var inner = ex.InnerException;
            while (inner is not null)
            {
                if (inner is Npgsql.PostgresException pg && pg.SqlState == "23505"
                    && string.Equals(pg.ConstraintName, constraintName, StringComparison.Ordinal))
                    return true;
                inner = inner.InnerException;
            }
            return false;
        }

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
            // So Natureza=Venda e receita: Perda/Doacao/Ajuste com preco nao entram no caixa.
            var q = db.Vendas.AsNoTracking()
                .Where(v => v.EmpresaId == empresaId && v.Natureza == NaturezaMovimentacaoEstoque.Venda
                            && v.DataVenda >= iniUtc && v.DataVenda < fimUtc);
            if (lojaId.HasValue) q = q.Where(v => v.LojaId == lojaId);

            // Projeta so a coluna ValorTotal (VO Dinheiro via HasConversion — o EF nao
            // traduz Sum() sobre VO-com-converter, entao a soma fica em memoria, mas o
            // SELECT deixa de trazer a entidade Venda inteira). Antes: SELECT * + materializa Venda.
            var valores = await q.Select(v => v.ValorTotal).ToListAsync();
            var bruto = valores.Sum(v => v == null ? 0m : v.Valor);

            // Estorno de saida devolve o dinheiro no dia do estorno (fluxo de caixa): abate das vendas
            // estornadas dentro da janela, independentemente do dia em que a venda foi feita.
            var qe = db.MovimentacoesEstoque.AsNoTracking()
                .Where(m => m.EmpresaId == empresaId && m.VendaId != null
                            && m.Tipo == TipoMovimentacaoEstoque.Saida && m.Natureza == NaturezaMovimentacaoEstoque.Venda
                            && m.EstornadaEm != null && m.EstornadaEm >= iniUtc && m.EstornadaEm < fimUtc);
            if (lojaId.HasValue)
                qe = qe.Where(m => db.Vendas.Any(v => v.Id == m.VendaId && v.LojaId == lojaId));
            var estornados = await qe.Select(m => m.ValorTotal).ToListAsync();
            var estornado = estornados.Sum(v => v == null ? 0m : v.Valor);

            return bruto - estornado;
        }

        public Task<decimal> GetTotalPagamentosPedidosDoDiaAsync(Guid empresaId, DateOnly data, Guid? lojaId = null)
        {
            var (inicio, fim) = HorarioBrasil.JanelaDiaUtc(data);
            return GetTotalPagamentosPedidosNoIntervaloAsync(empresaId, inicio, fim, lojaId);
        }

        public async Task<decimal> GetTotalPagamentosPedidosNoIntervaloAsync(Guid empresaId, DateTime iniUtc, DateTime fimUtc, Guid? lojaId = null)
        {
            // pg.Valor e decimal simples -> SumAsync agrega no proprio Postgres (antes:
            // materializava a lista de pagamentos do intervalo e somava em memoria).
            return await db.Set<PedidoPagamento>().AsNoTracking()
                .Where(pg => pg.PagoEm >= iniUtc && pg.PagoEm < fimUtc)
                .Join(db.Pedidos.AsNoTracking(),
                      pg => pg.PedidoId,
                      p => p.Id,
                      (pg, p) => new { pg, p })
                .Where(x => x.p.EmpresaId == empresaId
                         // Cancelar a operação não devolve dinheiro. Só sai do saldo quando
                         // o recebimento tem estorno confirmado na cobrança correspondente.
                         && !db.Set<CobrancaPedido>().Any(c => c.EmpresaId == empresaId
                             && c.PedidoId == x.p.Id && c.Status == StatusCobrancaPedido.Estornada
                             && x.pg.Referencia != null && c.PagamentoExternoId == x.pg.Referencia)
                         // So pedidos SEM Venda consolidada (balcao/web tem so PedidoPagamento).
                         // Pedido mobile entregue gera Venda (VendaId setado) e ja e contado por
                         // GetTotalVendas — sem este filtro o mesmo dinheiro somava 2x no caixa (#926).
                         && x.p.VendaId == null
                         && (lojaId == null || x.p.LojaId == lojaId))
                .SumAsync(x => (decimal?)x.pg.Valor) ?? 0m;
        }

        // ── Linhas para a lista "Movimentos do dia" (BUG-5) ───────────
        // Mesma seleção das queries de total acima (mesma janela/filtros), mas devolve as linhas
        // individuais — garante que a soma das linhas exibidas == total somado ao saldo.

        public async Task<IReadOnlyList<Venda>> GetVendasNoIntervaloAsync(Guid empresaId, DateTime iniUtc, DateTime fimUtc, Guid? lojaId = null)
        {
            var q = db.Vendas.AsNoTracking()
                .Where(v => v.EmpresaId == empresaId && v.Natureza == NaturezaMovimentacaoEstoque.Venda
                            && v.DataVenda >= iniUtc && v.DataVenda < fimUtc);
            if (lojaId.HasValue) q = q.Where(v => v.LojaId == lojaId);
            return await q.OrderBy(v => v.DataVenda).ToListAsync();
        }

        public async Task<IReadOnlyList<PedidoPagamento>> GetPagamentosPedidosListaNoIntervaloAsync(Guid empresaId, DateTime iniUtc, DateTime fimUtc, Guid? lojaId = null)
        {
            return await db.Set<PedidoPagamento>().AsNoTracking()
                .Where(pg => pg.PagoEm >= iniUtc && pg.PagoEm < fimUtc)
                .Join(db.Pedidos.AsNoTracking(),
                      pg => pg.PedidoId,
                      p => p.Id,
                      (pg, p) => new { pg, p })
                .Where(x => x.p.EmpresaId == empresaId
                         && !db.Set<CobrancaPedido>().Any(c => c.EmpresaId == empresaId
                             && c.PedidoId == x.p.Id && c.Status == StatusCobrancaPedido.Estornada
                             && x.pg.Referencia != null && c.PagamentoExternoId == x.pg.Referencia)
                         // Mesmo filtro do total (#926): exclui pagamentos de pedidos com Venda
                         // consolidada, para a soma das linhas exibidas casar com o SaldoEsperado.
                         && x.p.VendaId == null
                         && (lojaId == null || x.p.LojaId == lojaId))
                .OrderBy(x => x.pg.PagoEm)
                .Select(x => x.pg)
                .ToListAsync();
        }
    }
}

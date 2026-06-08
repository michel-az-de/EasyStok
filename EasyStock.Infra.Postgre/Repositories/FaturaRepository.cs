using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories;

public sealed class FaturaRepository(EasyStockDbContext db) : IFaturaRepository
{
    public Task AddAsync(Fatura fatura, CancellationToken ct = default) =>
        db.Faturas.AddAsync(fatura, ct).AsTask();

    public Task UpdateAsync(Fatura fatura, CancellationToken ct = default)
    {
        // ADR-0028 / BUG-01 (#512): a fatura sempre chega RASTREADA (carregada via
        // GetByIdAsync no mesmo DbContext scoped); o change tracker ja persiste as
        // mudancas (filho novo como Added, raiz como Modified) no CommitAsync. Chamar
        // db.Faturas.Update() aqui rebaixaria filhos novos (PK preenchida) a Modified
        // -> UPDATE em linha inexistente -> DbUpdateConcurrencyException. Detached com
        // filhos novos nao e suportado: fail-fast em vez de reintroduzir o bug.
        if (db.Entry(fatura).State == EntityState.Detached)
            throw new InvalidOperationException(
                "FaturaRepository.UpdateAsync requer uma fatura rastreada (carregue via GetByIdAsync). " +
                "Grafo detached com filhos novos nao e suportado — confie no change tracker (ADR-0028).");
        return Task.CompletedTask;
    }

    // IgnoreQueryFilters: este metodo e chamado por webhooks anonimos (RegistrarPagamentoFatura
    // disparado pelo WebhookPixController) e jobs em background sem JWT. Nesses contextos
    // CurrentTenantId == Guid.Empty e IsSuperAdmin == false, entao o filter global zeraria o
    // resultado. Multi-tenancy fica garantido pelo Where manual `f.EmpresaId == empresaId` —
    // defesa em profundidade conforme conventions.md.
    public Task<Fatura?> GetByIdAsync(Guid empresaId, Guid faturaId, CancellationToken ct = default) =>
        db.Faturas
            .IgnoreQueryFilters()
            .Include(f => f.Itens.OrderBy(i => i.Ordem))
            .Include(f => f.Pagamentos)
            .Include(f => f.Eventos.OrderByDescending(e => e.OcorridoEm))
            .FirstOrDefaultAsync(f => f.EmpresaId == empresaId && f.Id == faturaId, ct);

    public Task<FaturaPagamento?> ObterPagamentoPorClientIdempotencyKeyAsync(
        Guid empresaId, string clientIdempotencyKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(clientIdempotencyKey)) return Task.FromResult<FaturaPagamento?>(null);
        return db.FaturaPagamentos
            .FirstOrDefaultAsync(p =>
                p.EmpresaId == empresaId &&
                p.ClientIdempotencyKey == clientIdempotencyKey, ct);
    }

    // IgnoreQueryFilters honra a semantica documentada na interface ("sem filtro EmpresaId").
    // Sem isso, admin operacional caia silenciosamente no filter global e via comportamento
    // diferente de SuperAdmin (que tem bypass IsSuperAdmin). Caller (controller admin) ja faz
    // checagem explicita de tenant para admin operacional.
    public Task<Fatura?> GetByIdAdminAsync(Guid faturaId, CancellationToken ct = default) =>
        db.Faturas
            .IgnoreQueryFilters()
            .Include(f => f.Empresa)
            .Include(f => f.Cliente)
            .Include(f => f.Itens.OrderBy(i => i.Ordem))
            .Include(f => f.Pagamentos)
            .Include(f => f.Eventos.OrderByDescending(e => e.OcorridoEm))
            .FirstOrDefaultAsync(f => f.Id == faturaId, ct);

    public async Task<(IReadOnlyList<Fatura> Itens, int Total)> ListarClienteAsync(
        Guid empresaId,
        StatusFatura? status = null,
        DateTime? vencimentoDe = null,
        DateTime? vencimentoAte = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        var q = db.Faturas
            .AsNoTracking()
            .Where(f => f.EmpresaId == empresaId);

        q = AplicarFiltros(q, status, null, vencimentoDe, vencimentoAte, null, null, null);

        var total = await q.CountAsync(ct);

        var itens = await q
            .OrderByDescending(f => f.DataEmissao)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (itens, total);
    }

    public async Task<(IReadOnlyList<Fatura> Itens, int Total)> ListarAdminAsync(
        Guid? empresaId = null,
        StatusFatura? status = null,
        OrigemFatura? origem = null,
        DateTime? vencimentoDe = null,
        DateTime? vencimentoAte = null,
        decimal? valorMin = null,
        decimal? valorMax = null,
        string? busca = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        var q = db.Faturas
            .AsNoTracking()
            .Include(f => f.Empresa)
            .AsQueryable();

        if (empresaId.HasValue && empresaId.Value != Guid.Empty)
            q = q.Where(f => f.EmpresaId == empresaId.Value);

        q = AplicarFiltros(q, status, origem, vencimentoDe, vencimentoAte, valorMin, valorMax, busca);

        var total = await q.CountAsync(ct);

        var itens = await q
            .OrderByDescending(f => f.DataEmissao)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (itens, total);
    }

    // IgnoreQueryFilters: usado pela idempotencia de EmitirFaturaUseCase. O job de cobranca
    // (CobrancaAssinaturaJob) chama em background sem JWT — sem isso retornaria null mesmo
    // existindo fatura, gerando duplicidade. Tenant fica protegido pelo Where manual.
    public Task<Fatura?> GetByOrigemAsync(Guid empresaId, OrigemFatura origem, Guid origemRefId, CancellationToken ct = default) =>
        db.Faturas
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                f => f.EmpresaId == empresaId
                  && f.Origem == origem
                  && f.OrigemRefId == origemRefId
                  && f.Status != StatusFatura.Cancelada,
                ct);

    // ─── F10 — Metricas ───────────────────────────────────────────────

    public async Task<IReadOnlyDictionary<StatusFatura, int>> ContarPorStatusAsync(
        DateTime de, DateTime ate, Guid? empresaId = null, CancellationToken ct = default)
    {
        var q = db.Faturas
            .IgnoreQueryFilters()
            .Where(f => f.DataEmissao >= de && f.DataEmissao < ate);
        if (empresaId.HasValue && empresaId.Value != Guid.Empty)
            q = q.Where(f => f.EmpresaId == empresaId.Value);

        var rows = await q
            .GroupBy(f => f.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.Status, r => r.Count);
    }

    public async Task<IReadOnlyDictionary<StatusFatura, decimal>> SomarTotalPorStatusAsync(
        DateTime de, DateTime ate, Guid? empresaId = null, CancellationToken ct = default)
    {
        var q = db.Faturas
            .IgnoreQueryFilters()
            .Where(f => f.DataEmissao >= de && f.DataEmissao < ate);
        if (empresaId.HasValue && empresaId.Value != Guid.Empty)
            q = q.Where(f => f.EmpresaId == empresaId.Value);

        var rows = await q
            .GroupBy(f => f.Status)
            .Select(g => new { Status = g.Key, Sum = g.Sum(f => f.Total) })
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.Status, r => r.Sum);
    }

    public async Task<double> MediaDiasAtrasoVencidasAsync(Guid? empresaId = null, CancellationToken ct = default)
    {
        var hoje = DateTime.UtcNow.Date;
        var q = db.Faturas
            .IgnoreQueryFilters()
            .Where(f => f.Status == StatusFatura.Vencida && f.DataPagamentoTotal == null);
        if (empresaId.HasValue && empresaId.Value != Guid.Empty)
            q = q.Where(f => f.EmpresaId == empresaId.Value);

        // EF nao traduz EF.Functions.DateDiff facilmente cross-provider — tras pra memoria.
        var datasVencimento = await q.Select(f => f.DataVencimento).ToListAsync(ct);
        if (datasVencimento.Count == 0) return 0d;

        return datasVencimento.Average(dv => (hoje - dv.Date).TotalDays);
    }

    public async Task<IReadOnlyList<TopInadimplenteResult>> TopInadimplentesAsync(
        int limit = 5, Guid? empresaId = null, CancellationToken ct = default)
    {
        if (limit < 1) limit = 5;
        if (limit > 50) limit = 50;

        var q = db.Faturas
            .IgnoreQueryFilters()
            .Where(f => f.Status == StatusFatura.Vencida);
        if (empresaId.HasValue && empresaId.Value != Guid.Empty)
            q = q.Where(f => f.EmpresaId == empresaId.Value);

        var rows = await q
            .GroupBy(f => f.EmpresaId)
            .Select(g => new
            {
                EmpresaId = g.Key,
                Qtd = g.Count(),
                Valor = g.Sum(f => f.Total)
            })
            .OrderByDescending(r => r.Qtd)
            .ThenByDescending(r => r.Valor)
            .Take(limit)
            .ToListAsync(ct);

        if (rows.Count == 0) return Array.Empty<TopInadimplenteResult>();

        var ids = rows.Select(r => r.EmpresaId).ToList();
        var nomes = await db.Empresas
            .IgnoreQueryFilters()
            .Where(e => ids.Contains(e.Id))
            .Select(e => new { e.Id, e.Nome })
            .ToDictionaryAsync(e => e.Id, e => (string?)e.Nome, ct);

        return rows.Select(r => new TopInadimplenteResult(
            r.EmpresaId,
            nomes.TryGetValue(r.EmpresaId, out var n) ? n : null,
            r.Qtd,
            r.Valor
        )).ToList();
    }

    private static IQueryable<Fatura> AplicarFiltros(
        IQueryable<Fatura> q,
        StatusFatura? status,
        OrigemFatura? origem,
        DateTime? vencimentoDe,
        DateTime? vencimentoAte,
        decimal? valorMin,
        decimal? valorMax,
        string? busca)
    {
        if (status.HasValue)
            q = q.Where(f => f.Status == status.Value);
        if (origem.HasValue)
            q = q.Where(f => f.Origem == origem.Value);
        if (vencimentoDe.HasValue)
            q = q.Where(f => f.DataVencimento >= vencimentoDe.Value);
        if (vencimentoAte.HasValue)
            q = q.Where(f => f.DataVencimento <= vencimentoAte.Value);
        if (valorMin.HasValue)
            q = q.Where(f => f.Total >= valorMin.Value);
        if (valorMax.HasValue)
            q = q.Where(f => f.Total <= valorMax.Value);
        if (!string.IsNullOrWhiteSpace(busca))
        {
            var b = busca.Trim();
            q = q.Where(f => f.Numero.Contains(b) || (f.Observacoes != null && f.Observacoes.Contains(b)));
        }
        return q;
    }
}

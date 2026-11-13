using EasyStock.Application.Common;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Enums.Financeiro;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories;

/// <param name="relogio">Relógio do "hoje" do filtro de status (#2095, F12); nulo é o do sistema.</param>
public sealed class ContaPagarRepository(EasyStockDbContext db, TimeProvider? relogio = null) : IContaPagarRepository
{
    public Task AddAsync(ContaPagar conta, CancellationToken ct = default)
    {
        db.ContasPagar.Add(conta);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(ContaPagar conta, CancellationToken ct = default)
    {
        // #1769: `Update(conta)` marcava como Modified todo filho com chave preenchida, e a chave do pagamento e
        // da parcela nasce no app. Registrar pagamento e adicionar parcela viravam UPDATE em linha que nao existe:
        // "dados alterados por outro processo" (409) em toda tentativa. Filho novo entra como INSERT antes de o
        // EF olhar o pai (mesmo defeito do BUG-01 #512 em Faturas, ADR-0028).
        // A detecção automática fica desligada durante a conferência: ligada, olhar a primeira parcela já
        // descobre a parcela nova pela coleção da conta e a marca como Modified antes de chegarmos nela.
        var detectar = db.ChangeTracker.AutoDetectChangesEnabled;
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            foreach (var parcela in conta.Parcelas)
            {
                foreach (var pagamento in parcela.Pagamentos)
                    if (db.Entry(pagamento).State == EntityState.Detached) db.Add(pagamento);
                if (db.Entry(parcela).State == EntityState.Detached) db.ParcelasPagar.Add(parcela);
            }
            if (db.Entry(conta).State == EntityState.Detached) db.ContasPagar.Update(conta);
        }
        finally
        {
            db.ChangeTracker.AutoDetectChangesEnabled = detectar;
        }
        return Task.CompletedTask;
    }

    public Task<ContaPagar?> GetByIdAsync(Guid empresaId, Guid id, CancellationToken ct = default)
        => db.ContasPagar
            .Include(c => c.Parcelas)
            .FirstOrDefaultAsync(c => c.EmpresaId == empresaId && c.Id == id, ct);

    public Task<ContaPagar?> GetByIdWithDetailsAsync(Guid empresaId, Guid id, CancellationToken ct = default)
        => db.ContasPagar
            .Include(c => c.Parcelas).ThenInclude(p => p.Pagamentos)
            .Include(c => c.Categoria)
            .Include(c => c.CentroCusto)
            .Include(c => c.Fornecedor)
            .Include(c => c.Alteracoes.OrderByDescending(a => a.AlteradoEm))
            .AsSplitQuery()
            .FirstOrDefaultAsync(c => c.EmpresaId == empresaId && c.Id == id, ct);

    public Task<ContaPagar?> GetByOrigemAsync(Guid empresaId, OrigemContaFinanceira origem, Guid origemRefId, CancellationToken ct = default)
        => db.ContasPagar
            .FirstOrDefaultAsync(c =>
                c.EmpresaId == empresaId &&
                c.Origem == origem &&
                c.OrigemRefId == origemRefId, ct);

    public Task<ContaPagar?> GetByDocumentoReferenciaAsync(Guid empresaId, string documentoReferencia, CancellationToken ct = default)
        => db.ContasPagar
            .FirstOrDefaultAsync(c =>
                c.EmpresaId == empresaId &&
                c.DocumentoReferencia == documentoReferencia, ct);

    public async Task<(IReadOnlyList<ContaPagar> Itens, int Total)> ListarAsync(
        Guid empresaId,
        StatusContaFinanceira? status = null,
        Guid? fornecedorId = null,
        Guid? categoriaId = null,
        Guid? centroCustoId = null,
        DateTime? vencimentoDe = null,
        DateTime? vencimentoAte = null,
        string? busca = null,
        int page = 1,
        int pageSize = 20,
        string? sort = "datavencimento",
        string? order = "asc",
        CancellationToken ct = default)
    {
        var query = db.ContasPagar.AsNoTracking()
            .Where(c => c.EmpresaId == empresaId);

        if (status.HasValue)
        {
            // Filtro consistente com o badge derivado (BUG-022): Vencida = armazenada
            // Vencida OU Aberta/Parcial com parcela vencida por data; Aberta/Parcial
            // excluem as ja vencidas. Demais status: match direto no armazenado.
            // #2095 (F12): o "hoje" é o dia de Brasília, igual ao StatusEfetivo da pílula; com UtcNow.Date a lista e a
            // pílula discordavam entre 21h e 24h. DataVencimento é data civil (00:00Z ou 03:00Z do dia).
            var hojeFiltro = HorarioBrasil.CivilComoInstanteUtc(
                HorarioBrasil.DataOperacional((relogio ?? TimeProvider.System).GetUtcNow().UtcDateTime));
            query = status.Value switch
            {
                StatusContaFinanceira.Vencida => query.Where(c =>
                    c.Status == StatusContaFinanceira.Vencida ||
                    ((c.Status == StatusContaFinanceira.Aberta || c.Status == StatusContaFinanceira.ParcialmentePaga) &&
                     c.Parcelas.Any(p => p.Status != StatusParcela.Paga && p.Status != StatusParcela.Cancelada && p.DataVencimento < hojeFiltro))),
                StatusContaFinanceira.Aberta => query.Where(c =>
                    c.Status == StatusContaFinanceira.Aberta &&
                    !c.Parcelas.Any(p => p.Status != StatusParcela.Paga && p.Status != StatusParcela.Cancelada && p.DataVencimento < hojeFiltro)),
                StatusContaFinanceira.ParcialmentePaga => query.Where(c =>
                    c.Status == StatusContaFinanceira.ParcialmentePaga &&
                    !c.Parcelas.Any(p => p.Status != StatusParcela.Paga && p.Status != StatusParcela.Cancelada && p.DataVencimento < hojeFiltro)),
                _ => query.Where(c => c.Status == status.Value),
            };
        }
        if (fornecedorId.HasValue) query = query.Where(c => c.FornecedorId == fornecedorId.Value);
        if (categoriaId.HasValue) query = query.Where(c => c.CategoriaFinanceiraId == categoriaId.Value);
        if (centroCustoId.HasValue) query = query.Where(c => c.CentroCustoId == centroCustoId.Value);

        if (vencimentoDe.HasValue || vencimentoAte.HasValue)
        {
            // Filtro por vencimento de QUALQUER parcela ativa no range
            var subParcelas = db.ParcelasPagar.AsNoTracking()
                .Where(p => p.EmpresaId == empresaId && p.Status != StatusParcela.Cancelada);
            if (vencimentoDe.HasValue) subParcelas = subParcelas.Where(p => p.DataVencimento >= vencimentoDe.Value);
            if (vencimentoAte.HasValue) subParcelas = subParcelas.Where(p => p.DataVencimento <= vencimentoAte.Value);
            var contaIds = subParcelas.Select(p => p.ContaPagarId);
            query = query.Where(c => contaIds.Contains(c.Id));
        }

        if (!string.IsNullOrWhiteSpace(busca))
        {
            var padrao = PadraoILike.Contendo(busca);
            query = query.Where(c =>
                EF.Functions.ILike(c.Descricao, padrao, PadraoILike.Escape) ||
                (c.DocumentoReferencia != null && EF.Functions.ILike(c.DocumentoReferencia, padrao, PadraoILike.Escape)) ||
                (c.Observacoes != null && EF.Functions.ILike(c.Observacoes, padrao, PadraoILike.Escape)));
        }

        var total = await query.CountAsync(ct);
        var desc = string.Equals(order, "desc", StringComparison.OrdinalIgnoreCase);

        query = (sort?.ToLowerInvariant()) switch
        {
            "valor" => (desc ? query.OrderByDescending(c => c.ValorTotal) : query.OrderBy(c => c.ValorTotal)).ThenBy(c => c.Id),
            "criadoem" => (desc ? query.OrderByDescending(c => c.CriadoEm) : query.OrderBy(c => c.CriadoEm)).ThenBy(c => c.Id),
            "status" => (desc ? query.OrderByDescending(c => c.Status) : query.OrderBy(c => c.Status)).ThenBy(c => c.Id),
            "dataemissao" => (desc ? query.OrderByDescending(c => c.DataEmissao) : query.OrderBy(c => c.DataEmissao)).ThenBy(c => c.Id),
            // #2095 (F11): "datavencimento" (padrao) e o proximo vencimento em aberto: menor vencimento das parcelas nao
            // pagas e nao canceladas. Conta sem parcela aberta vai para o fim nos dois sentidos; Id desempata a pagina.
            _ => OrdenarPorProximoVencimento(query, desc),
        };

        var itens = await query
            .Include(c => c.Parcelas)
            .Skip((Math.Max(1, page) - 1) * Math.Clamp(pageSize, 1, 200))
            .Take(Math.Clamp(pageSize, 1, 200))
            .ToListAsync(ct);

        return (itens, total);
    }

    private static IOrderedQueryable<ContaPagar> OrdenarPorProximoVencimento(IQueryable<ContaPagar> query, bool desc)
    {
        var semAberta = query.OrderBy(c => !c.Parcelas.Any(p => p.Status != StatusParcela.Paga && p.Status != StatusParcela.Cancelada));
        var porVencimento = desc
            ? semAberta.ThenByDescending(c => c.Parcelas
                .Where(p => p.Status != StatusParcela.Paga && p.Status != StatusParcela.Cancelada)
                .Min(p => (DateTime?)p.DataVencimento))
            : semAberta.ThenBy(c => c.Parcelas
                .Where(p => p.Status != StatusParcela.Paga && p.Status != StatusParcela.Cancelada)
                .Min(p => (DateTime?)p.DataVencimento));
        return porVencimento.ThenBy(c => c.Id);
    }

    public Task<ParcelaPagar?> GetParcelaAsync(Guid empresaId, Guid parcelaId, CancellationToken ct = default)
        => db.ParcelasPagar
            .Include(p => p.Pagamentos)
            .FirstOrDefaultAsync(p => p.EmpresaId == empresaId && p.Id == parcelaId, ct);

    public Task<ParcelaPagar?> GetParcelaWithContaAsync(Guid empresaId, Guid parcelaId, CancellationToken ct = default)
        => db.ParcelasPagar
            .Include(p => p.Pagamentos)
            .Include(p => p.ContaPagar!).ThenInclude(c => c.Parcelas).ThenInclude(p2 => p2.Pagamentos)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.EmpresaId == empresaId && p.Id == parcelaId, ct);

    public Task AddEventoAsync(ContaFinanceiraEvento evento, CancellationToken ct = default)
    {
        db.ContasFinanceirasEventos.Add(evento);
        return Task.CompletedTask;
    }

    public Task AddAlteracaoAsync(ContaPagarAlteracao alteracao, CancellationToken ct = default)
    {
        db.ContaPagarAlteracoes.Add(alteracao);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<ParcelaPagar>> ListarParcelasParaJobVencimentoAsync(
        DateTime hojeUtc, CancellationToken ct = default)
    {
        var ate = hojeUtc.Date.AddDays(3); // D-3 cohort
        return await db.ParcelasPagar
            .IgnoreQueryFilters()
            .Where(p =>
                (p.Status == StatusParcela.Pendente ||
                 p.Status == StatusParcela.ParcialmentePaga ||
                 p.Status == StatusParcela.Vencida) &&
                p.DataVencimento.Date <= ate.Date)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyDictionary<StatusContaFinanceira, decimal>> SomarPorStatusAsync(
        Guid empresaId, DateTime de, DateTime ate, CancellationToken ct = default)
    {
        var rows = await db.ContasPagar
            .AsNoTracking()
            .Where(c => c.EmpresaId == empresaId &&
                        c.DataEmissao >= de &&
                        c.DataEmissao <= ate)
            .GroupBy(c => c.Status)
            .Select(g => new { Status = g.Key, Total = g.Sum(c => c.ValorTotal) })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.Status, r => r.Total);
    }
}

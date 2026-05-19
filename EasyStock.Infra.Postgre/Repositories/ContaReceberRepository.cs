using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Enums.Financeiro;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories;

public sealed class ContaReceberRepository(EasyStockDbContext db) : IContaReceberRepository
{
    public Task AddAsync(ContaReceber conta, CancellationToken ct = default)
    {
        db.ContasReceber.Add(conta);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(ContaReceber conta, CancellationToken ct = default)
    {
        db.ContasReceber.Update(conta);
        return Task.CompletedTask;
    }

    public Task<ContaReceber?> GetByIdAsync(Guid empresaId, Guid id, CancellationToken ct = default)
        => db.ContasReceber
            .Include(c => c.Parcelas)
            .FirstOrDefaultAsync(c => c.EmpresaId == empresaId && c.Id == id, ct);

    public Task<ContaReceber?> GetByIdWithDetailsAsync(Guid empresaId, Guid id, CancellationToken ct = default)
        => db.ContasReceber
            .Include(c => c.Parcelas).ThenInclude(p => p.Pagamentos)
            .Include(c => c.Categoria)
            .Include(c => c.CentroCusto)
            .Include(c => c.Cliente)
            .Include(c => c.Alteracoes.OrderByDescending(a => a.AlteradoEm))
            .AsSplitQuery()
            .FirstOrDefaultAsync(c => c.EmpresaId == empresaId && c.Id == id, ct);

    public Task<ContaReceber?> GetByOrigemAsync(Guid empresaId, OrigemContaFinanceira origem, Guid origemRefId, CancellationToken ct = default)
        => db.ContasReceber
            .FirstOrDefaultAsync(c =>
                c.EmpresaId == empresaId &&
                c.Origem == origem &&
                c.OrigemRefId == origemRefId, ct);

    public Task<ContaReceber?> GetByDocumentoReferenciaAsync(Guid empresaId, string documentoReferencia, CancellationToken ct = default)
        => db.ContasReceber
            .FirstOrDefaultAsync(c =>
                c.EmpresaId == empresaId &&
                c.DocumentoReferencia == documentoReferencia, ct);

    public async Task<(IReadOnlyList<ContaReceber> Itens, int Total)> ListarAsync(
        Guid empresaId,
        StatusContaFinanceira? status = null,
        Guid? clienteId = null,
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
        var query = db.ContasReceber.AsNoTracking()
            .Where(c => c.EmpresaId == empresaId);

        if (status.HasValue) query = query.Where(c => c.Status == status.Value);
        if (clienteId.HasValue) query = query.Where(c => c.ClienteId == clienteId.Value);
        if (categoriaId.HasValue) query = query.Where(c => c.CategoriaFinanceiraId == categoriaId.Value);
        if (centroCustoId.HasValue) query = query.Where(c => c.CentroCustoId == centroCustoId.Value);

        if (vencimentoDe.HasValue || vencimentoAte.HasValue)
        {
            var subParcelas = db.ParcelasReceber.AsNoTracking()
                .Where(p => p.EmpresaId == empresaId && p.Status != StatusParcela.Cancelada);
            if (vencimentoDe.HasValue) subParcelas = subParcelas.Where(p => p.DataVencimento >= vencimentoDe.Value);
            if (vencimentoAte.HasValue) subParcelas = subParcelas.Where(p => p.DataVencimento <= vencimentoAte.Value);
            var contaIds = subParcelas.Select(p => p.ContaReceberId);
            query = query.Where(c => contaIds.Contains(c.Id));
        }

        if (!string.IsNullOrWhiteSpace(busca))
        {
            var termo = busca.Trim();
            query = query.Where(c =>
                EF.Functions.ILike(c.Descricao, $"%{termo}%") ||
                (c.DocumentoReferencia != null && EF.Functions.ILike(c.DocumentoReferencia, $"%{termo}%")) ||
                (c.Observacoes != null && EF.Functions.ILike(c.Observacoes, $"%{termo}%")));
        }

        var total = await query.CountAsync(ct);
        var desc = string.Equals(order, "desc", StringComparison.OrdinalIgnoreCase);

        query = (sort?.ToLowerInvariant()) switch
        {
            "valor" => desc ? query.OrderByDescending(c => c.ValorTotal) : query.OrderBy(c => c.ValorTotal),
            "criadoem" => desc ? query.OrderByDescending(c => c.CriadoEm) : query.OrderBy(c => c.CriadoEm),
            "status" => desc ? query.OrderByDescending(c => c.Status) : query.OrderBy(c => c.Status),
            _ => desc ? query.OrderByDescending(c => c.DataEmissao) : query.OrderBy(c => c.DataEmissao),
        };

        var itens = await query
            .Include(c => c.Parcelas)
            .Skip((Math.Max(1, page) - 1) * Math.Clamp(pageSize, 1, 200))
            .Take(Math.Clamp(pageSize, 1, 200))
            .ToListAsync(ct);

        return (itens, total);
    }

    public Task<ParcelaReceber?> GetParcelaAsync(Guid empresaId, Guid parcelaId, CancellationToken ct = default)
        => db.ParcelasReceber
            .Include(p => p.Pagamentos)
            .FirstOrDefaultAsync(p => p.EmpresaId == empresaId && p.Id == parcelaId, ct);

    public Task<ParcelaReceber?> GetParcelaWithContaAsync(Guid empresaId, Guid parcelaId, CancellationToken ct = default)
        => db.ParcelasReceber
            .Include(p => p.Pagamentos)
            .Include(p => p.ContaReceber!).ThenInclude(c => c.Parcelas).ThenInclude(p2 => p2.Pagamentos)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.EmpresaId == empresaId && p.Id == parcelaId, ct);

    public Task<ParcelaReceber?> GetParcelaByEfiTxidAsync(string txid, CancellationToken ct = default)
        => db.ParcelasReceber
            .IgnoreQueryFilters()
            .Include(p => p.Pagamentos)
            .Include(p => p.ContaReceber)
            .FirstOrDefaultAsync(p => p.EfiTxid == txid, ct);

    public Task AddEventoAsync(ContaFinanceiraEvento evento, CancellationToken ct = default)
    {
        db.ContasFinanceirasEventos.Add(evento);
        return Task.CompletedTask;
    }

    public Task AddAlteracaoAsync(ContaReceberAlteracao alteracao, CancellationToken ct = default)
    {
        db.ContaReceberAlteracoes.Add(alteracao);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<ParcelaReceber>> ListarParcelasParaJobVencimentoAsync(
        DateTime hojeUtc, CancellationToken ct = default)
    {
        var ate = hojeUtc.Date.AddDays(3);
        return await db.ParcelasReceber
            .IgnoreQueryFilters()
            .Where(p =>
                (p.Status == StatusParcela.Pendente ||
                 p.Status == StatusParcela.ParcialmentePaga ||
                 p.Status == StatusParcela.Vencida) &&
                p.DataVencimento.Date <= ate.Date)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ParcelaReceber>> ListarParcelasComPixAtivoAsync(
        DateTime hojeUtc, CancellationToken ct = default)
    {
        var horizonte = hojeUtc.AddDays(-90); // limite de estorno Pix
        return await db.ParcelasReceber
            .IgnoreQueryFilters()
            .Where(p =>
                p.EfiTxid != null &&
                (p.Status == StatusParcela.Pendente || p.Status == StatusParcela.ParcialmentePaga) &&
                (p.PixExpiraEm == null || p.PixExpiraEm > horizonte))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyDictionary<StatusContaFinanceira, decimal>> SomarPorStatusAsync(
        Guid empresaId, DateTime de, DateTime ate, CancellationToken ct = default)
    {
        var rows = await db.ContasReceber
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

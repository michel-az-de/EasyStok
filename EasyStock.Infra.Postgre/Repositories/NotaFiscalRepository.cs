using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities.Fiscal;
using EasyStock.Domain.Enums.Fiscal;
using EasyStock.Domain.ValueObjects.Fiscal;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories;

public sealed class NotaFiscalRepository(EasyStockDbContext db) : INotaFiscalRepository
{
    public Task<NotaFiscal?> ObterPorIdAsync(Guid empresaId, Guid id, CancellationToken ct) =>
        db.NotasFiscais
            .Where(n => n.EmpresaId == empresaId && n.Id == id)
            .SingleOrDefaultAsync(ct);

    public Task<NotaFiscal?> ObterPorIdComItensAsync(Guid empresaId, Guid id, CancellationToken ct) =>
        db.NotasFiscais
            .Include(n => n.Itens)
            .Include(n => n.Pagamentos)
            .Include(n => n.Eventos)
            .Where(n => n.EmpresaId == empresaId && n.Id == id)
            .SingleOrDefaultAsync(ct);

    public Task<NotaFiscal?> ObterPorIdempotencyKeyAsync(Guid empresaId, string key, CancellationToken ct) =>
        db.NotasFiscais
            .Include(n => n.Itens)
            .Include(n => n.Pagamentos)
            .Where(n => n.EmpresaId == empresaId && n.IdempotencyKey == key)
            .SingleOrDefaultAsync(ct);

    public async Task<NotaFiscal?> ObterPorChaveAsync(string chaveAcesso, CancellationToken ct)
    {
        // Webhook do Focus chega sem contexto de tenant — IgnoreQueryFilters é necessario.
        // Caller deve usar nota.EmpresaId para qualquer mutação subsequente.
        var chave = ChaveAcessoNFe.Parse(chaveAcesso);
        return await db.NotasFiscais
            .IgnoreQueryFilters()
            .Where(n => n.ChaveAcesso == chave)
            .SingleOrDefaultAsync(ct);
    }

    public async Task<(IReadOnlyList<NotaFiscal> Items, int Total)> ListarAsync(
        Guid empresaId,
        Guid? lojaId,
        DateTime? desdeUtc,
        DateTime? ateUtc,
        StatusNotaFiscal? status,
        string? chaveAcesso,
        int pagina,
        int tamanhoPagina,
        CancellationToken ct)
    {
        if (pagina < 1) pagina = 1;
        if (tamanhoPagina is < 1 or > 200) tamanhoPagina = 30;

        var q = db.NotasFiscais.AsNoTracking().Where(n => n.EmpresaId == empresaId);

        if (lojaId is not null) q = q.Where(n => n.LojaId == lojaId);
        if (desdeUtc is not null) q = q.Where(n => n.DataEmissao >= desdeUtc);
        if (ateUtc is not null) q = q.Where(n => n.DataEmissao <= ateUtc);
        if (status is not null) q = q.Where(n => n.Status == status);
        if (!string.IsNullOrWhiteSpace(chaveAcesso))
        {
            var chave = ChaveAcessoNFe.Parse(chaveAcesso);
            q = q.Where(n => n.ChaveAcesso == chave);
        }

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderByDescending(n => n.DataEmissao)
            .Skip((pagina - 1) * tamanhoPagina)
            .Take(tamanhoPagina)
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<IReadOnlyList<NotaFiscal>> ListarEmContingenciaAsync(int limit, CancellationToken ct)
    {
        var corte = DateTime.UtcNow.AddHours(-24);
        return await db.NotasFiscais
            .IgnoreQueryFilters()
            .Where(n => n.Status == StatusNotaFiscal.EmContingencia && n.DataEmissao > corte)
            .OrderBy(n => n.DataEmissao)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<int>> ListarNumerosUsadosAsync(
        Guid empresaId, Guid lojaId, ModeloDocumentoFiscal modelo, int serie,
        int de, int ate, CancellationToken ct)
    {
        return await db.NotasFiscais
            .AsNoTracking()
            .Where(n => n.EmpresaId == empresaId
                     && n.LojaId == lojaId
                     && n.Modelo == modelo
                     && n.Serie == serie
                     && n.Numero >= de
                     && n.Numero <= ate)
            .Select(n => n.Numero)
            .ToListAsync(ct);
    }

    public Task AdicionarAsync(NotaFiscal nota, CancellationToken ct)
    {
        return db.NotasFiscais.AddAsync(nota, ct).AsTask();
    }

    public Task AtualizarAsync(NotaFiscal nota, CancellationToken ct)
    {
        db.NotasFiscais.Update(nota);
        return Task.CompletedTask;
    }

    public Task AdicionarInutilizacaoAsync(NotaFiscalInutilizacao inut, CancellationToken ct)
    {
        return db.NotasFiscaisInutilizacoes.AddAsync(inut, ct).AsTask();
    }

    public Task AtualizarInutilizacaoAsync(NotaFiscalInutilizacao inut, CancellationToken ct)
    {
        db.NotasFiscaisInutilizacoes.Update(inut);
        return Task.CompletedTask;
    }

    public Task<NotaFiscalInutilizacao?> ObterInutilizacaoPorIdAsync(Guid empresaId, Guid id, CancellationToken ct) =>
        db.NotasFiscaisInutilizacoes
            .Where(x => x.EmpresaId == empresaId && x.Id == id)
            .SingleOrDefaultAsync(ct);

    public async Task<IReadOnlyList<NotaFiscalInutilizacao>> ListarInutilizacoesAsync(
        Guid empresaId, Guid? lojaId, int? ano, CancellationToken ct)
    {
        var q = db.NotasFiscaisInutilizacoes
            .AsNoTracking()
            .Where(x => x.EmpresaId == empresaId);
        if (lojaId is not null) q = q.Where(x => x.LojaId == lojaId);
        if (ano is not null) q = q.Where(x => x.Ano == ano);
        return await q.OrderByDescending(x => x.CriadoEm).ToListAsync(ct);
    }
}

using EasyStock.Application.Ports.Output.Persistence.Campanhas;
using EasyStock.Domain.Entities.Campanhas;
using EasyStock.Domain.Enums.Campanhas;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories.Campanhas;

/// <summary>Campanhas (S28). <c>EmpresaId</c> no WHERE além do filtro global e do RLS (ADR-0010).</summary>
public sealed class CampanhaRepository(EasyStockDbContext db) : ICampanhaRepository
{
    public Task AddAsync(Campanha campanha, CancellationToken ct = default) =>
        db.Campanhas.AddAsync(campanha, ct).AsTask();

    public Task<Campanha?> ObterAsync(Guid empresaId, Guid id, CancellationToken ct = default) =>
        db.Campanhas.FirstOrDefaultAsync(c => c.EmpresaId == empresaId && c.Id == id, ct);

    public async Task<IReadOnlyList<Campanha>> ListarAsync(
        Guid empresaId, StatusCampanha? status, int limite, CancellationToken ct = default)
    {
        var query = db.Campanhas.AsNoTracking().Where(c => c.EmpresaId == empresaId);
        if (status is { } s) query = query.Where(c => c.Status == s);
        return await query.OrderByDescending(c => c.CriadaEm).Take(Math.Clamp(limite, 1, 500)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<CampanhaDestinatario>> ListarPendentesAsync(
        Guid empresaId, Guid campanhaId, CancellationToken ct = default) =>
        await db.CampanhaDestinatarios
            .Where(d => d.EmpresaId == empresaId && d.CampanhaId == campanhaId
                && d.Status == StatusCampanhaDestinatario.Pendente)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<CampanhaDestinatario>> ListarDestinatariosAsync(
        Guid empresaId, Guid campanhaId, CancellationToken ct = default) =>
        await db.CampanhaDestinatarios
            .Where(d => d.EmpresaId == empresaId && d.CampanhaId == campanhaId)
            .ToListAsync(ct);

    public Task AddDestinatariosAsync(IEnumerable<CampanhaDestinatario> destinatarios, CancellationToken ct = default) =>
        db.CampanhaDestinatarios.AddRangeAsync(destinatarios, ct);

    public void RemoverDestinatarios(IEnumerable<CampanhaDestinatario> destinatarios) =>
        db.CampanhaDestinatarios.RemoveRange(destinatarios);

    public async Task<IReadOnlyList<EnvioDestinatarioCampanha>> ListarEnfileiradosAsync(
        Guid empresaId, Guid campanhaId, CancellationToken ct = default)
    {
        var linhas = await (
                from d in db.CampanhaDestinatarios
                where d.EmpresaId == empresaId && d.CampanhaId == campanhaId
                    && d.Status == StatusCampanhaDestinatario.Enfileirado
                join m in db.NotifOutboxMensagens.Where(m => m.EmpresaId == empresaId)
                    on d.OutboxMensagemId equals (Guid?)m.Id into mensagens
                from m in mensagens.DefaultIfEmpty()
                select new { Destinatario = d, Mensagem = m })
            .ToListAsync(ct);
        return linhas.Select(l => new EnvioDestinatarioCampanha(l.Destinatario, l.Mensagem)).ToList();
    }

    public async Task<IReadOnlyList<CampanhaDestinatario>> ListarEnviadosAsync(
        Guid empresaId, Guid campanhaId, CancellationToken ct = default) =>
        await db.CampanhaDestinatarios
            .Where(d => d.EmpresaId == empresaId && d.CampanhaId == campanhaId
                && d.Status == StatusCampanhaDestinatario.Enviado)
            .ToListAsync(ct);

    public Task<CampanhaDestinatario?> ObterEnviadoParaAtribuirAsync(
        Guid empresaId, Guid clienteId, DateTime desde, CancellationToken ct = default) =>
        db.CampanhaDestinatarios
            .Where(d => d.EmpresaId == empresaId && d.ClienteId == clienteId
                && d.Status == StatusCampanhaDestinatario.Enviado && d.EnviadoEm >= desde
                && db.Campanhas.Any(c => c.EmpresaId == empresaId && c.Id == d.CampanhaId
                    && (c.Status == StatusCampanha.Enviando || c.Status == StatusCampanha.Enviada)))
            .OrderByDescending(d => d.EnviadoEm)
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// Cross-tenant por desenho (o job atende todas as empresas), como a reserva das mensagens
    /// programadas (S39): <c>IgnoreQueryFilters</c> e o bypass de RLS ligado por quem chama.
    /// </summary>
    public async Task<IReadOnlyList<CampanhaParaProcessar>> ListarParaProcessarAsync(
        DateTime agora, int limite, CancellationToken ct = default) =>
        await db.Campanhas
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => (c.Status == StatusCampanha.Agendada && c.DisparoEm <= agora)
                || c.Status == StatusCampanha.Enviando
                || (c.Status == StatusCampanha.Enviada && c.EncerramentoEm <= agora)
                || db.CampanhaDestinatarios.IgnoreQueryFilters().Any(d => d.EmpresaId == c.EmpresaId
                    && d.CampanhaId == c.Id && d.Status == StatusCampanhaDestinatario.Enfileirado))
            .OrderBy(c => c.DisparoEm)
            .Take(Math.Clamp(limite, 1, 500))
            .Select(c => new CampanhaParaProcessar(c.EmpresaId, c.Id))
            .ToListAsync(ct);
}

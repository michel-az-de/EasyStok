using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories;

/// <summary>
/// Implementacao Postgres do <see cref="IWebhookRecebidoRepository"/>.
/// Evento ja registrado em <c>(Provedor, EventIdExterno)</c> retorna null: fluxo padrao de idempotencia.
/// </summary>
public sealed class WebhookRecebidoRepository(EasyStockDbContext db) : IWebhookRecebidoRepository
{
    public async Task<WebhookRecebido?> TryRegistrarAsync(
        string provedor,
        string eventIdExterno,
        string rawBodyHash,
        CancellationToken ct = default)
    {
        var entity = WebhookRecebido.Criar(provedor, eventIdExterno, rawBodyHash);
        // #3229: a reentrega e o caminho normal. Com Add + SaveChanges, o 23505 era capturado aqui, mas o EF ja tinha
        // logado 2 linhas Error por evento repetido. ON CONFLICT nao falha, nao loga e nao aborta a transacao.
        var inseridas = await db.Database.ExecuteSqlAsync($"""
            INSERT INTO webhook_recebidos ("Id", "Provedor", "EventIdExterno", "RawBodyHash", "RecebidoEm", "Sucesso")
            VALUES ({entity.Id}, {entity.Provedor}, {entity.EventIdExterno}, {entity.RawBodyHash}, {entity.RecebidoEm}, {entity.Sucesso})
            ON CONFLICT ("Provedor", "EventIdExterno") DO NOTHING
            """, ct);
        return inseridas == 1 ? entity : null;
    }

    public Task<WebhookRecebido?> ObterAsync(string provedor, string eventIdExterno, CancellationToken ct = default) =>
        db.WebhookRecebidos.FirstOrDefaultAsync(
            w => w.Provedor == provedor && w.EventIdExterno == eventIdExterno, ct);

    public Task<DateTime?> UltimoRecebidoEmAsync(string provedor, string prefixoEventId, CancellationToken ct = default) =>
        db.WebhookRecebidos.AsNoTracking()
            .Where(w => w.Provedor == provedor && w.EventIdExterno.StartsWith(prefixoEventId))
            .MaxAsync(w => (DateTime?)w.RecebidoEm, ct);

    public Task<int> ExpurgarAsync(string provedor, DateTime antesDe, CancellationToken ct = default) =>
        db.WebhookRecebidos.Where(w => w.Provedor == provedor && w.RecebidoEm < antesDe).ExecuteDeleteAsync(ct);

    public async Task MarcarProcessadoAsync(Guid id, bool sucesso, string? erro = null, CancellationToken ct = default)
    {
        // Falhou: o que o processamento deixou rastreado pertence a uma transação revertida. Sem descartar, o
        // SaveChanges abaixo gravaria esse resto junto com a marca de falha (#1562).
        if (!sucesso) db.DescartarAlteracoesPendentes();

        var existing = await db.WebhookRecebidos.FirstOrDefaultAsync(w => w.Id == id, ct);
        if (existing is null) return;
        existing.MarcarProcessado(sucesso, erro);
        await db.SaveChangesAsync(ct);
    }
}

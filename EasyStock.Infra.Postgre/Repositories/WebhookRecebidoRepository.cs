using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;
using Npgsql;

namespace EasyStock.Infra.Postgre.Repositories;

/// <summary>
/// Implementacao Postgres do <see cref="IWebhookRecebidoRepository"/>.
/// Detecta violacao de UNIQUE em <c>(Provedor, EventIdExterno)</c> via
/// PostgresException.SqlState 23505 e retorna null — fluxo padrao de
/// idempotencia.
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
        try
        {
            await db.WebhookRecebidos.AddAsync(entity, ct);
            await db.SaveChangesAsync(ct);
            return entity;
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // Outro processo (ou retry do gateway) ja registrou este mesmo evento.
            // Detacha para o context nao tentar de novo no proximo SaveChanges.
            db.Entry(entity).State = EntityState.Detached;
            return null;
        }
    }

    public async Task MarcarProcessadoAsync(Guid id, bool sucesso, string? erro = null, CancellationToken ct = default)
    {
        var existing = await db.WebhookRecebidos.FirstOrDefaultAsync(w => w.Id == id, ct);
        if (existing is null) return;
        existing.MarcarProcessado(sucesso, erro);
        await db.SaveChangesAsync(ct);
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        // SQLSTATE 23505 = unique_violation em PostgreSQL.
        return ex.InnerException is PostgresException pg && pg.SqlState == "23505";
    }
}

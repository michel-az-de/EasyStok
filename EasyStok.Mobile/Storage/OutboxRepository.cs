using System.Text.Json;

namespace EasyStok.Mobile.Storage;

/// <summary>
/// Append-only fila de mutations a flushar via REST. Cada item carrega
/// o tipo (discriminator) e o payload JSON serializado. O flush remove
/// itens 2xx e incrementa Attempts/LastError em falha.
/// </summary>
public sealed class OutboxRepository : IOutboxRepository
{
	private readonly AppDatabase _db;

	public OutboxRepository(AppDatabase db)
	{
		_db = db;
	}

	public async Task<int> EnqueueAsync<T>(string type, T payload)
	{
		var conn = await _db.GetConnectionAsync();
		var json = JsonSerializer.Serialize(payload);
		var item = new OutboxItem
		{
			Type = type,
			PayloadJson = json,
			CreatedAtUtc = DateTime.UtcNow,
		};
		await conn.InsertAsync(item);
		return item.Id;
	}

	public async Task<IReadOnlyList<OutboxItem>> GetPendingAsync(int max = 50)
	{
		var conn = await _db.GetConnectionAsync();
		return await conn.Table<OutboxItem>()
			.OrderBy(x => x.Id)
			.Take(max)
			.ToListAsync();
	}

	public async Task DeleteAsync(int id)
	{
		var conn = await _db.GetConnectionAsync();
		await conn.DeleteAsync<OutboxItem>(id);
	}

	public async Task RecordFailureAsync(int id, string error)
	{
		var conn = await _db.GetConnectionAsync();
		var item = await conn.FindAsync<OutboxItem>(id);
		if (item is null) return;
		item.Attempts++;
		item.LastAttemptAtUtc = DateTime.UtcNow;
		item.LastError = error.Length > 500 ? error[..500] : error;
		await conn.UpdateAsync(item);
	}

	public async Task<int> CountAsync()
	{
		var conn = await _db.GetConnectionAsync();
		return await conn.Table<OutboxItem>().CountAsync();
	}
}

public interface IOutboxRepository
{
	Task<int> EnqueueAsync<T>(string type, T payload);
	Task<IReadOnlyList<OutboxItem>> GetPendingAsync(int max = 50);
	Task DeleteAsync(int id);
	Task RecordFailureAsync(int id, string error);
	Task<int> CountAsync();
}

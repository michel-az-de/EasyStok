using SQLite;

namespace EasyStok.Mobile.Storage;

/// <summary>
/// Wrapper singleton sobre <see cref="SQLiteAsyncConnection"/>. Cria e
/// migra as tabelas locais (cache + queue + audit log) na primeira vez
/// que e usada. As tabelas espelham os DTOs da API ERP — last-write-wins
/// no cache, append-only na queue.
/// </summary>
public sealed class AppDatabase
{
	private const string DbFileName = "easystok.db3";
	private SQLiteAsyncConnection? _connection;
	private readonly SemaphoreSlim _initLock = new(1, 1);

	public async Task<SQLiteAsyncConnection> GetConnectionAsync()
	{
		if (_connection is not null) return _connection;

		await _initLock.WaitAsync();
		try
		{
			if (_connection is not null) return _connection;

			var path = Path.Combine(FileSystem.Current.AppDataDirectory, DbFileName);
			var conn = new SQLiteAsyncConnection(path,
				SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);

			await conn.CreateTableAsync<CachedItemEstoque>();
			await conn.CreateTableAsync<CachedPedido>();
			await conn.CreateTableAsync<CachedCliente>();
			await conn.CreateTableAsync<CachedCaixaEntry>();
			await conn.CreateTableAsync<OutboxItem>();
			await conn.CreateTableAsync<AuditLogEntry>();
			await conn.CreateTableAsync<KvMeta>();

			_connection = conn;
			return _connection;
		}
		finally
		{
			_initLock.Release();
		}
	}
}

// =============================================================================
// Cache local — espelha o ItemEstoqueDto da API. Reescrito a cada Pull.
// =============================================================================
public sealed class CachedItemEstoque
{
	[PrimaryKey]
	public string Id { get; set; } = string.Empty;

	[Indexed]
	public string ProdutoId { get; set; } = string.Empty;

	public string Sku { get; set; } = string.Empty;
	public string ProdutoNome { get; set; } = string.Empty;
	public string? Emoji { get; set; }
	public string? CategoriaId { get; set; }
	public int Qty { get; set; }
	public string Status { get; set; } = "ok";
	public DateTime LastMovUtc { get; set; }
	public DateTime? ValidadeUtc { get; set; }
	public string? Lote { get; set; }
	public decimal CustoUnitario { get; set; }
	public decimal? PrecoVendaSugerido { get; set; }

	[Indexed]
	public Guid EmpresaId { get; set; }

	public DateTime CachedAtUtc { get; set; }
}

// =============================================================================
// Cache local de pedidos (kanban + finalizados + conferencia).
// =============================================================================
public sealed class CachedPedido
{
	[PrimaryKey]
	public string Id { get; set; } = string.Empty;

	public string? ClienteId { get; set; }
	public string ClienteNome { get; set; } = string.Empty;

	[Indexed]
	public string Status { get; set; } = "aguardando"; // aguardando | preparando | pronto | entregue | cancelado

	public decimal Total { get; set; }
	public DateTime CriadoUtc { get; set; }
	public DateTime AtualizadoUtc { get; set; }
	public string? ShortCode { get; set; }
	public string? ItensJson { get; set; }

	[Indexed]
	public Guid EmpresaId { get; set; }

	public DateTime CachedAtUtc { get; set; }
}

// =============================================================================
// Cache local de clientes.
// =============================================================================
public sealed class CachedCliente
{
	[PrimaryKey]
	public string Id { get; set; } = string.Empty;

	public string Nome { get; set; } = string.Empty;
	public string? Telefone { get; set; }
	public DateTime? UltimoPedidoUtc { get; set; }
	public int TotalPedidos { get; set; }

	[Indexed]
	public Guid EmpresaId { get; set; }

	public DateTime CachedAtUtc { get; set; }
}

// =============================================================================
// Caixa do dia — entradas e saidas.
// =============================================================================
public sealed class CachedCaixaEntry
{
	[PrimaryKey]
	public string Id { get; set; } = string.Empty;

	public string Tipo { get; set; } = "entrada"; // entrada | saida
	public decimal Valor { get; set; }
	public string Descricao { get; set; } = string.Empty;

	[Indexed]
	public DateTime AtUtc { get; set; }

	[Indexed]
	public Guid EmpresaId { get; set; }

	public DateTime CachedAtUtc { get; set; }
}

// =============================================================================
// Outbox — fila de mutations a flushar via REST (substitui localStorage queue).
// Append-only; cada item vai para o backend via POST/PATCH e remove daqui ao
// receber 2xx. F4 vai consumir.
// =============================================================================
public sealed class OutboxItem
{
	[PrimaryKey, AutoIncrement]
	public int Id { get; set; }

	/// <summary>"estoque.entrada", "estoque.saida", "pedido.criar", etc.</summary>
	[Indexed]
	public string Type { get; set; } = string.Empty;

	/// <summary>JSON serializado do command/payload.</summary>
	public string PayloadJson { get; set; } = string.Empty;

	public DateTime CreatedAtUtc { get; set; }
	public int Attempts { get; set; }
	public DateTime? LastAttemptAtUtc { get; set; }
	public string? LastError { get; set; }
}

// =============================================================================
// Audit log local (cap 1000) — espelha o cdb-audit-log do PWA.
// =============================================================================
public sealed class AuditLogEntry
{
	[PrimaryKey, AutoIncrement]
	public int Id { get; set; }

	[Indexed]
	public DateTime AtUtc { get; set; }

	public string Type { get; set; } = string.Empty;
	public string Description { get; set; } = string.Empty;
	public string? Operator { get; set; }
}

// =============================================================================
// Key-value para metadados pequenos (lastSyncAt, schemaVersion, etc.)
// =============================================================================
public sealed class KvMeta
{
	[PrimaryKey]
	public string Key { get; set; } = string.Empty;
	public string Value { get; set; } = string.Empty;
}

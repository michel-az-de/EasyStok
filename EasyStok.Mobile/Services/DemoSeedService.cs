using EasyStok.Mobile.Storage;

namespace EasyStok.Mobile.Services;

/// <summary>
/// Modo demo offline — popula o cache local com produtos de exemplo
/// quando o usuario entra via "Continuar offline (modo demo)" sem
/// backend disponivel. Permite explorar o app E2E sem rede.
/// IDs sao Guids fixos para que o mesmo set rode em qualquer device.
/// </summary>
public sealed class DemoSeedService : IDemoSeedService
{
	// Guids fixos publicos pra distinguir o tenant demo de empresas reais.
	public static readonly Guid DemoEmpresaId = Guid.Parse("11111111-1111-1111-1111-111111111111");
	public static readonly Guid DemoLojaId = Guid.Parse("22222222-2222-2222-2222-222222222222");
	public static readonly Guid DemoUsuarioId = Guid.Parse("33333333-3333-3333-3333-333333333333");

	private readonly AppDatabase _db;

	public DemoSeedService(AppDatabase db)
	{
		_db = db;
	}

	public async Task SeedIfEmptyAsync()
	{
		var conn = await _db.GetConnectionAsync();
		var existing = await conn.Table<CachedItemEstoque>()
			.Where(x => x.EmpresaId == DemoEmpresaId)
			.CountAsync();
		if (existing > 0) return;

		var now = DateTime.UtcNow;
		var seed = new[]
		{
			MakeItem("a1", "p1", "PAO-FRA",  "Pao frances",          "🥖", 28, "ok",       7),
			MakeItem("a2", "p2", "CAFE-500", "Cafe torrado 500g",    "☕", 14, "ok",       45),
			MakeItem("a3", "p3", "LEITE-1L", "Leite integral 1L",    "🥛", 6,  "atencao",  4),
			MakeItem("a4", "p4", "ACUC-1KG", "Acucar refinado 1kg",  "🧂", 22, "ok",       180),
			MakeItem("a5", "p5", "MANT-200", "Manteiga 200g",        "🧈", 3,  "critico",  10),
			MakeItem("a6", "p6", "OVO-DZ",   "Ovos branco duzia",    "🥚", 9,  "ok",       14),
			MakeItem("a7", "p7", "QUEIJ-K",  "Queijo minas 1kg",     "🧀", 0,  "vencido",  -2),
		};

		foreach (var item in seed)
			item.CachedAtUtc = now;

		await conn.RunInTransactionAsync(c =>
		{
			foreach (var item in seed) c.InsertOrReplace(item);
		});
	}

	private static CachedItemEstoque MakeItem(string idSuffix, string produtoSuffix, string sku, string nome, string emoji, int qty, string status, int diasParaValidade)
	{
		var validade = diasParaValidade > 0
			? DateTime.UtcNow.Date.AddDays(diasParaValidade)
			: DateTime.UtcNow.Date.AddDays(diasParaValidade); // negativo = vencido

		return new CachedItemEstoque
		{
			Id = $"00000000-0000-0000-0000-00000000{idSuffix.PadLeft(4, '0')}",
			ProdutoId = $"00000000-0000-0000-0000-00000000{produtoSuffix.PadLeft(4, '0')}",
			Sku = sku,
			ProdutoNome = nome,
			Emoji = emoji,
			Qty = qty,
			Status = status,
			LastMovUtc = DateTime.UtcNow.AddDays(-1),
			ValidadeUtc = validade,
			CustoUnitario = 0m,
			EmpresaId = DemoEmpresaId,
		};
	}
}

public interface IDemoSeedService
{
	Task SeedIfEmptyAsync();
}

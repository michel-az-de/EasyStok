using EasyStok.Mobile.Storage;

namespace EasyStok.Mobile.Services;

/// <summary>
/// Modo demo offline — popula o cache local com dados de exemplo
/// (estoque, clientes, pedidos, caixa, audit log) quando o usuario
/// entra via "Continuar offline" sem backend disponivel. Permite
/// explorar o app E2E sem rede. Guids fixos pra ser idempotente.
/// </summary>
public sealed class DemoSeedService : IDemoSeedService
{
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
        await SeedEstoqueIfEmptyAsync();
        await SeedClientesIfEmptyAsync();
        await SeedPedidosIfEmptyAsync();
        await SeedCaixaIfEmptyAsync();
        await SeedAuditLogIfEmptyAsync();
    }

    private async Task SeedEstoqueIfEmptyAsync()
    {
        var conn = await _db.GetConnectionAsync();
        var existing = await conn.Table<CachedItemEstoque>().Where(x => x.EmpresaId == DemoEmpresaId).CountAsync();
        if (existing > 0) return;

        var now = DateTime.UtcNow;
        var seed = new[]
        {
            MakeEstoque("a1", "p1", "PAO-FRA",  "Pão francês",          "🥖", 28, "ok",       7),
            MakeEstoque("a2", "p2", "CAFE-500", "Café torrado 500g",    "☕", 14, "ok",       45),
            MakeEstoque("a3", "p3", "LEITE-1L", "Leite integral 1L",    "🥛", 6,  "atencao",  4),
            MakeEstoque("a4", "p4", "ACUC-1KG", "Açúcar refinado 1kg",  "🧂", 22, "ok",       180),
            MakeEstoque("a5", "p5", "MANT-200", "Manteiga 200g",        "🧈", 3,  "critico",  10),
            MakeEstoque("a6", "p6", "OVO-DZ",   "Ovos brancos dúzia",   "🥚", 9,  "ok",       14),
            MakeEstoque("a7", "p7", "QUEIJ-K",  "Queijo minas 1kg",     "🧀", 0,  "vencido",  -2),
        };
        foreach (var item in seed) item.CachedAtUtc = now;
        await conn.RunInTransactionAsync(c => { foreach (var i in seed) c.InsertOrReplace(i); });
    }

    private async Task SeedClientesIfEmptyAsync()
    {
        var conn = await _db.GetConnectionAsync();
        var existing = await conn.Table<CachedCliente>().Where(x => x.EmpresaId == DemoEmpresaId).CountAsync();
        if (existing > 0) return;

        var now = DateTime.UtcNow;
        var seed = new[]
        {
            new CachedCliente { Id = "c1", Nome = "Ana Souza",      Telefone = "(11) 91111-1111", TotalPedidos = 5, UltimoPedidoUtc = now.AddDays(-1),  EmpresaId = DemoEmpresaId, CachedAtUtc = now },
            new CachedCliente { Id = "c2", Nome = "Bruno Lima",     Telefone = "(11) 92222-2222", TotalPedidos = 2, UltimoPedidoUtc = now.AddDays(-7),  EmpresaId = DemoEmpresaId, CachedAtUtc = now },
            new CachedCliente { Id = "c3", Nome = "Carla Mendes",   Telefone = "(11) 93333-3333", TotalPedidos = 12, UltimoPedidoUtc = now.AddHours(-3), EmpresaId = DemoEmpresaId, CachedAtUtc = now },
            new CachedCliente { Id = "c4", Nome = "Diego Pereira",  Telefone = "(11) 94444-4444", TotalPedidos = 1, UltimoPedidoUtc = now.AddDays(-30), EmpresaId = DemoEmpresaId, CachedAtUtc = now },
            new CachedCliente { Id = "c5", Nome = "Erica Cardoso",  Telefone = "(11) 95555-5555", TotalPedidos = 8, UltimoPedidoUtc = now.AddDays(-2),  EmpresaId = DemoEmpresaId, CachedAtUtc = now },
        };
        await conn.RunInTransactionAsync(c => { foreach (var i in seed) c.InsertOrReplace(i); });
    }

    private async Task SeedPedidosIfEmptyAsync()
    {
        var conn = await _db.GetConnectionAsync();
        var existing = await conn.Table<CachedPedido>().Where(x => x.EmpresaId == DemoEmpresaId).CountAsync();
        if (existing > 0) return;

        var now = DateTime.UtcNow;
        var seed = new[]
        {
            MakePedido("o1", "c1", "Ana Souza",     "aguardando", 24.00m, now.AddMinutes(-5),   "EW001"),
            MakePedido("o2", "c3", "Carla Mendes",  "aguardando", 56.50m, now.AddMinutes(-12),  "EW002"),
            MakePedido("o3", "c2", "Bruno Lima",    "preparando", 18.00m, now.AddMinutes(-30),  "EW003"),
            MakePedido("o4", "c5", "Erica Cardoso", "preparando", 72.00m, now.AddMinutes(-25),  "EW004"),
            MakePedido("o5", "c4", "Diego Pereira", "pronto",     35.00m, now.AddHours(-1),     "EW005"),
            MakePedido("o6", "c1", "Ana Souza",     "entregue",   42.00m, now.AddDays(-1),      "EW006"),
        };
        await conn.RunInTransactionAsync(c => { foreach (var i in seed) c.InsertOrReplace(i); });
    }

    private async Task SeedCaixaIfEmptyAsync()
    {
        var conn = await _db.GetConnectionAsync();
        var existing = await conn.Table<CachedCaixaEntry>().Where(x => x.EmpresaId == DemoEmpresaId).CountAsync();
        if (existing > 0) return;

        var now = DateTime.UtcNow;
        var seed = new[]
        {
            new CachedCaixaEntry { Id = "k1", Tipo = "entrada", Valor = 24.00m,  Descricao = "Venda balcão #EW001", AtUtc = now.AddHours(-4), EmpresaId = DemoEmpresaId, CachedAtUtc = now },
            new CachedCaixaEntry { Id = "k2", Tipo = "entrada", Valor = 56.50m,  Descricao = "Venda PIX #EW002",    AtUtc = now.AddHours(-3), EmpresaId = DemoEmpresaId, CachedAtUtc = now },
            new CachedCaixaEntry { Id = "k3", Tipo = "saida",   Valor = 80.00m,  Descricao = "Compra de leite",     AtUtc = now.AddHours(-2), EmpresaId = DemoEmpresaId, CachedAtUtc = now },
            new CachedCaixaEntry { Id = "k4", Tipo = "entrada", Valor = 35.00m,  Descricao = "Venda balcão #EW005", AtUtc = now.AddHours(-1), EmpresaId = DemoEmpresaId, CachedAtUtc = now },
        };
        await conn.RunInTransactionAsync(c => { foreach (var i in seed) c.InsertOrReplace(i); });
    }

    private async Task SeedAuditLogIfEmptyAsync()
    {
        var conn = await _db.GetConnectionAsync();
        var existing = await conn.Table<AuditLogEntry>().CountAsync();
        if (existing > 0) return;

        var now = DateTime.UtcNow;
        var seed = new[]
        {
            new AuditLogEntry { AtUtc = now.AddDays(-1).AddHours(-2), Type = "login",         Description = "Login realizado",                         Operator = "Demo" },
            new AuditLogEntry { AtUtc = now.AddDays(-1).AddHours(-1), Type = "estoque",       Description = "Entrada de Café torrado 500g (+10)",      Operator = "Demo" },
            new AuditLogEntry { AtUtc = now.AddHours(-6),             Type = "pedido",        Description = "Pedido EW001 criado para Ana",            Operator = "Demo" },
            new AuditLogEntry { AtUtc = now.AddHours(-5),             Type = "pedido",        Description = "Pedido EW001 → preparando",               Operator = "Demo" },
            new AuditLogEntry { AtUtc = now.AddHours(-4),             Type = "caixa",         Description = "Entrada R$ 24,00 (venda balcão)",         Operator = "Demo" },
            new AuditLogEntry { AtUtc = now.AddHours(-2),             Type = "caixa",         Description = "Saída R$ 80,00 (compra de leite)",        Operator = "Demo" },
            new AuditLogEntry { AtUtc = now.AddHours(-1),             Type = "estoque",       Description = "Saída de Manteiga 200g (-2)",             Operator = "Demo" },
            new AuditLogEntry { AtUtc = now.AddMinutes(-10),          Type = "pedido",        Description = "Pedido EW005 → pronto",                   Operator = "Demo" },
        };
        await conn.RunInTransactionAsync(c => { foreach (var i in seed) c.Insert(i); });
    }

    private static CachedItemEstoque MakeEstoque(string idSuffix, string produtoSuffix, string sku, string nome, string emoji, int qty, string status, int diasParaValidade) =>
        new()
        {
            Id = $"00000000-0000-0000-0000-00000000{idSuffix.PadLeft(4, '0')}",
            ProdutoId = $"00000000-0000-0000-0000-00000000{produtoSuffix.PadLeft(4, '0')}",
            Sku = sku,
            ProdutoNome = nome,
            Emoji = emoji,
            Qty = qty,
            Status = status,
            LastMovUtc = DateTime.UtcNow.AddDays(-1),
            ValidadeUtc = DateTime.UtcNow.Date.AddDays(diasParaValidade),
            CustoUnitario = 0m,
            EmpresaId = DemoEmpresaId,
        };

    private static CachedPedido MakePedido(string id, string clienteId, string clienteNome, string status, decimal total, DateTime atualizado, string shortCode) =>
        new()
        {
            Id = id,
            ClienteId = clienteId,
            ClienteNome = clienteNome,
            Status = status,
            Total = total,
            CriadoUtc = atualizado.AddMinutes(-5),
            AtualizadoUtc = atualizado,
            ShortCode = shortCode,
            EmpresaId = DemoEmpresaId,
            CachedAtUtc = DateTime.UtcNow,
        };
}

public interface IDemoSeedService
{
    Task SeedIfEmptyAsync();
}

using Npgsql;

var conn = Environment.GetEnvironmentVariable("PG_CONN")!;
await using var db = new NpgsqlConnection(conn);
await db.OpenAsync();

// Tentar inserir um ticket diretamente como o controller faria
var ticketId = Guid.NewGuid();
var empresaId = "00000000-0000-0000-0000-000000000000";
var atendenteId = "374796fd-5502-4787-b8d0-eff06b9422a8"; // SuperAdmin user

try
{
    await using var ins = new NpgsqlCommand(@"
        INSERT INTO admin_tickets (""Id"", ""EmpresaId"", ""Titulo"", ""Descricao"", ""Status"", ""Categoria"", ""Prioridade"", ""AtendenteId"", ""CriadoEm"", ""AlteradoEm"")
        VALUES (@id, @e::uuid, 'SQL Direct Test', 'Teste via SQL', 'Aberto', 'Bug', 'Normal', @a::uuid, NOW(), NOW())
    ", db);
    ins.Parameters.AddWithValue("id", ticketId);
    ins.Parameters.AddWithValue("e", empresaId);
    ins.Parameters.AddWithValue("a", atendenteId);
    var r = await ins.ExecuteNonQueryAsync();
    Console.WriteLine($"INSERT direto OK: {r} linha(s) afetada(s). TicketId={ticketId}");
}
catch (Exception ex)
{
    Console.WriteLine("INSERT direto FALHOU:");
    Console.WriteLine(ex.GetType().Name + ": " + ex.Message);
    if (ex is PostgresException pg)
    {
        Console.WriteLine($"  SqlState: {pg.SqlState}");
        Console.WriteLine($"  Detail: {pg.Detail}");
        Console.WriteLine($"  Constraint: {pg.ConstraintName}");
        Console.WriteLine($"  Column: {pg.ColumnName}");
    }
}

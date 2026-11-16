using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.IntegrationTests.Workflows;

// #2763: a trava do Caixa vista de fora, para os testes de quem deve esperar o fechamento.
//   TomarComoOFechamentoAsync(db, empresaId, lojaId): dentro de transação já aberta em db, toma a chave da empresa
//     e, se houver loja, a da loja, na ordem de CaixaRepository.TravarSaidasAsync.
//   EsperamATravaAsync(fixture, empresaId, operacoes, vigiadas): true quando operacoes ou mais estão na fila da chave
//     da EMPRESA (sondagem por conexão superusuário); false se alguma tarefa vigiada terminar antes ou passar de 30 s.
internal static class TravaDoCaixaTeste
{
    public static async Task TomarComoOFechamentoAsync(EasyStockDbContext db, Guid empresaId, Guid? lojaId)
    {
        await TravarAsync(db, $"caixa-saida:{empresaId:N}:");
        if (lojaId is { } loja) await TravarAsync(db, $"caixa-saida:{empresaId:N}:{loja:N}");
    }

    // pg_locks guarda a chave de 64 bits partida em classid (alta) e objid (baixa).
    public static async Task<bool> EsperamATravaAsync(
        PostgreSqlDatabaseFixture fixture, Guid empresaId, int operacoes, params Task[] vigiadas)
    {
        var chave = $"caixa-saida:{empresaId:N}:";
        await using var db = fixture.CreateDbContext();
        var limite = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < limite)
        {
            var esperando = await db.Database.SqlQuery<int>($@"SELECT count(*)::int AS ""Value"" FROM pg_locks
                WHERE locktype = 'advisory' AND NOT granted
                  AND objid::bigint = (hashtext({chave})::bigint & 4294967295)").SingleAsync();
            if (esperando >= operacoes) return true;
            if (vigiadas.Any(t => t.IsCompleted)) return false;
            await Task.Delay(50); // respiro da sonda, não é a sincronização
        }
        return false;
    }

    private static Task TravarAsync(EasyStockDbContext db, string chave) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({chave}))");
}

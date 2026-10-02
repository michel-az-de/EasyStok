using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Infra.Postgre.Repositories.Atendimento;
using FluentAssertions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// Reenvio (S57, #1355) em Postgres real: a reserva com <c>FOR UPDATE SKIP LOCKED</c> não deixa dois processos
/// reenviarem a mesma mensagem, e só entra mensagem que falhou com reenvio vencido.
/// </summary>
public class ReenvioMensagensIntegrationTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task DoisProcessosNaoReenviamAMesmaMensagem()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");

        var agora = DateTime.UtcNow;
        var empresa = Guid.NewGuid();
        var ids = new List<Guid>();
        await using (var db = fixture.CreateDbContext())
        {
            using var _ = db.UseRowLevelSecurityBypass();
            var conversa = Conversa.Abrir(empresa, "5511999990057", agora);
            db.AtendimentoConversas.Add(conversa);
            for (var i = 0; i < 3; i++)
            {
                var m = Mensagem.Saida(empresa, conversa.Id, AutorMensagem.Agente, agora, TipoConteudoMensagem.Texto, $"oi {i}");
                m.RegistrarFalhaEnvio("Meta fora", TipoFalhaEnvio.Temporaria, agora);
                db.AtendimentoMensagens.Add(m);
                ids.Add(m.Id);
            }
            var permanente = Mensagem.Saida(empresa, conversa.Id, AutorMensagem.Agente, agora, TipoConteudoMensagem.Texto, "não volta");
            permanente.RegistrarFalhaEnvio("fora da lista", TipoFalhaEnvio.Permanente, agora);
            db.AtendimentoMensagens.Add(permanente);
            await db.SaveChangesAsync();
        }

        var vence = agora.AddMinutes(2);
        await using var dbA = fixture.CreateDbContext();
        await using var dbB = fixture.CreateDbContext();
        using var bypassA = dbA.UseRowLevelSecurityBypass();
        using var bypassB = dbB.UseRowLevelSecurityBypass();

        await using var txA = await dbA.Database.BeginTransactionAsync();
        var pegasA = await new ConversaRepository(dbA).ListarReenviosVencidosComLockAsync(vence, 2);

        // A ainda segura o lock das duas primeiras: B só enxerga a terceira.
        await using var txB = await dbB.Database.BeginTransactionAsync();
        var pegasB = await new ConversaRepository(dbB).ListarReenviosVencidosComLockAsync(vence, 10);

        foreach (var m in pegasA.Concat(pegasB)) m.ReservarReenvio();
        await dbA.SaveChangesAsync();
        await txA.CommitAsync();
        await dbB.SaveChangesAsync();
        await txB.CommitAsync();

        var todas = pegasA.Select(m => m.Id).Concat(pegasB.Select(m => m.Id)).ToList();
        todas.Should().OnlyHaveUniqueItems("nenhuma mensagem pode ser reenviada pelos dois processos");
        todas.Should().BeEquivalentTo(ids, "a falha permanente não tem reenvio agendado");

        await using var dbC = fixture.CreateDbContext();
        using var bypassC = dbC.UseRowLevelSecurityBypass();
        (await new ConversaRepository(dbC).ListarReenviosVencidosComLockAsync(vence, 10))
            .Should().BeEmpty("depois de reservadas, saem da fila de reenvio");
    }
}

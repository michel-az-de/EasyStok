using EasyStock.Domain.Entities;
using EasyStock.Domain.Integration;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// #1575: o dispatcher do outbox de integração busca os pendentes numa consulta só. Ela devolve o mesmo
/// lote, na mesma ordem e com o mesmo limite das quatro consultas por shard que o laço fazia.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class OutboxEventoIntegracaoRepositoryIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    // Bem antigos: vêm antes de qualquer sobra de outro teste da coleção em cada shard.
    private static readonly DateTime Base = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [SkippableFact]
    public async Task PendentesDeTodosOsShards_MesmoLoteDasQuatroConsultasPorShard()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var empresa = Empresa.Criar("Outbox integração", null);

        OutboxEventoIntegracao Evento(int shard, int minuto, Action<OutboxEventoIntegracao>? ajuste = null)
        {
            var e = OutboxEventoIntegracao.Criar(empresa.Id, "pedido.teste1575", "Pedido", Guid.NewGuid(), "{}");
            e.ShardKey = shard;
            e.CriadoEm = Base.AddMinutes(minuto);
            e.ProximaTentativaEm = Base.AddMinutes(minuto);
            ajuste?.Invoke(e);
            return e;
        }
        // Fora de ordem de propósito: o mais velho de todos está no shard 3, que o laço só olhava por último.
        var s3 = Evento(3, 0);
        var s0b = Evento(0, 20);
        var s0a = Evento(0, 10);
        var s2 = Evento(2, 5);
        var s1 = Evento(1, 30);
        var agendado = Evento(1, 1, e => e.ProximaTentativaEm = DateTime.UtcNow.AddHours(1));
        var enviado = Evento(0, 2, e => e.Status = StatusOutboxIntegracao.Enviado);
        var emEnvio = Evento(2, 3, e => e.Status = StatusOutboxIntegracao.EmEnvio);
        await using (var seed = fixture.CreateDbContext())
        {
            seed.SetMobileTenantContext(empresa.Id);
            seed.Empresas.Add(empresa);
            seed.OutboxEventosIntegracao.AddRange(s3, s0b, s0a, s2, s1, agendado, enviado, emEnvio);
            await seed.SaveChangesAsync();
        }

        // Sem tenant no contexto, como no Worker.
        await using var db = fixture.CreateDbContext();
        var repo = new OutboxEventoIntegracaoRepository(db);

        foreach (var lote in new[] { 1, 3, 4, 1000 })
        {
            var novo = await repo.ProximosPendentesDeTodosOsShardsAsync(lote);

            var antigo = new List<OutboxEventoIntegracao>();
            for (var shard = 0; shard < 4 && antigo.Count < lote; shard++)
                antigo.AddRange(await repo.ProximosPendentesAsync(shard, lote - antigo.Count));

            novo.Select(e => e.Id).Should().Equal(antigo.Select(e => e.Id), "lote de {0}", lote);
        }

        var todos = await repo.ProximosPendentesDeTodosOsShardsAsync(1000);
        Guid[] meus = [s0a.Id, s0b.Id, s1.Id, s2.Id, s3.Id];
        todos.Select(e => e.Id).Where(meus.Contains).Should().Equal(meus, "por shard e, dentro dele, do mais antigo para o mais novo");
        todos.Select(e => e.Id).Should().NotContain([agendado.Id, enviado.Id, emEnvio.Id]);
    }
}

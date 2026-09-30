using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// CRM leve (S24) em Postgres real: AvisosStatusAtivos=false sobrevive ao INSERT (sentinela do default
/// true), tag é única por cliente, nota só aceita pedido do próprio cliente e as tabelas novas têm RLS.
/// </summary>
public class ClienteCrmIntegrationTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task PersisteBloqueioPreferenciasTagsENotas()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");

        var agora = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
        var empresa = Empresa.Criar("Casa da Baba CRM", "11111111000191");
        var semAvisos = Cliente.Criar(empresa.Id, "Sem avisos");
        semAvisos.DefinirAvisosStatus(false, agora);
        semAvisos.Bloquear("golpe", agora);
        var padrao = Cliente.Criar(empresa.Id, "Padrão");

        await using (var db = fixture.CreateDbContext())
        {
            using var _ = db.UseRowLevelSecurityBypass();
            await db.Database.MigrateAsync();
            db.Empresas.Add(empresa);
            db.Clientes.AddRange(semAvisos, padrao);
            semAvisos.AdicionarTag("Sem Glúten", OrigemClienteTag.Dona, agora);
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresa.Id);
            var repo = new ClienteCrmRepository(db);

            var lido = await repo.ObterComTagsAsync(empresa.Id, semAvisos.Id);
            lido!.AvisosStatusAtivos.Should().BeFalse("false não pode virar o default true do banco");
            lido.Bloqueado.Should().BeTrue();
            lido.MotivoBloqueio.Should().Be("golpe");
            lido.Tags.Should().ContainSingle().Which.Tag.Should().Be("sem_gluten");
            (await repo.ObterComTagsAsync(empresa.Id, padrao.Id))!.AvisosStatusAtivos.Should().BeTrue();

            // Unicidade no banco, além do no-op do domínio.
            AdicionarTagDuplicada(db, lido);
            var duplicar = () => db.SaveChangesAsync();
            await duplicar.Should().ThrowAsync<DbUpdateException>();
        }

        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresa.Id);
            var repo = new ClienteCrmRepository(db);

            (await repo.PedidoEhDoClienteAsync(empresa.Id, Guid.NewGuid(), semAvisos.Id)).Should().BeFalse();

            await repo.AdicionarNotaAsync(ClienteNota.Criar(empresa.Id, semAvisos.Id, "primeira", "Baba", agora));
            await repo.AdicionarNotaAsync(ClienteNota.Criar(empresa.Id, semAvisos.Id, "segunda", "agente", agora.AddMinutes(1)));
            await db.SaveChangesAsync();

            var notas = await repo.ListarNotasAsync(empresa.Id, semAvisos.Id, 10);
            notas.Select(n => n.Texto).Should().Equal("segunda", "primeira");

            var rls = await db.Database
                .SqlQueryRaw<bool>("""SELECT relrowsecurity AS "Value" FROM pg_class WHERE relname IN ('cliente_tags','cliente_notas')""")
                .ToListAsync();
            rls.Should().HaveCount(2).And.OnlyContain(ativo => ativo);
        }
    }

    /// <summary>Contorna o no-op do domínio para provar o índice único do banco.</summary>
    private static void AdicionarTagDuplicada(DbContext db, Cliente cliente)
    {
        var copia = Cliente.Criar(cliente.EmpresaId, "rascunho");
        var tag = copia.AdicionarTag("sem_gluten", OrigemClienteTag.Sistema, DateTime.UtcNow)!;
        db.Add(tag);
        db.Entry(tag).Property(nameof(ClienteTag.ClienteId)).CurrentValue = cliente.Id;
    }
}

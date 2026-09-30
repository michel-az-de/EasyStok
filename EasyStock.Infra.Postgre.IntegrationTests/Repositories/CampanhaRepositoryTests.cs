using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Campanhas;
using EasyStock.Domain.Enums.Campanhas;
using EasyStock.Infra.Postgre.Repositories.Campanhas;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// Campanhas (S28) em Postgres real: filtro de tenant e RLS nas duas tabelas, destinatário único por
/// (CampanhaId, ClienteId) e os pendentes que o cancelamento exclui.
/// </summary>
public class CampanhaRepositoryTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    private static readonly DateTime Agora = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);

    private static Campanha NovaCampanha(Guid empresaId, string nome) =>
        Campanha.Criar(empresaId, Guid.NewGuid(),
            new DadosCampanha(nome, "Oi {{nome}}", null, null, FiltroCampanha.ParaTodos, [], null, false, null),
            Agora);

    private async Task<(Campanha Campanha, Cliente Cliente)> SemearAsync(Guid empresaId, string nome)
    {
        var cliente = Cliente.Criar(empresaId, $"Cliente {nome}");
        var campanha = NovaCampanha(empresaId, nome);
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresaId);
        db.Empresas.Add(new Empresa
        {
            Id = empresaId,
            Nome = $"Empresa {nome}",
            Documento = empresaId.ToString("N")[..11],
            CriadoEm = Agora,
            AlteradoEm = Agora,
        });
        db.Clientes.Add(cliente);
        db.Campanhas.Add(campanha);
        db.CampanhaDestinatarios.Add(CampanhaDestinatario.Criar(campanha, cliente.Id));
        await db.SaveChangesAsync();
        return (campanha, cliente);
    }

    [SkippableFact]
    public async Task IsolamentoDeTenant()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresaA = Guid.NewGuid();
        var empresaB = Guid.NewGuid();
        var (campanhaA, _) = await SemearAsync(empresaA, "A");
        var (campanhaB, _) = await SemearAsync(empresaB, "B");

        await using var dbA = fixture.CreateDbContext();
        dbA.SetMobileTenantContext(empresaA);
        var repo = new CampanhaRepository(dbA);

        (await repo.ObterAsync(empresaA, campanhaB.Id)).Should().BeNull("campanha de outra empresa é invisível");
        (await repo.ObterAsync(empresaB, campanhaB.Id)).Should().BeNull("o filtro global barra mesmo com o EmpresaId certo no WHERE");
        (await repo.ListarAsync(empresaA, null, 100)).Select(c => c.Id).Should().Equal(campanhaA.Id);
        (await repo.ListarPendentesAsync(empresaA, campanhaB.Id)).Should().BeEmpty();
        (await dbA.CampanhaDestinatarios.CountAsync(d => d.CampanhaId == campanhaB.Id)).Should().Be(0);

        // RLS: login sem superuser, sem o filtro do EF; o banco sozinho barra a outra empresa.
        await using var rlsDb = fixture.CreateRlsClientDbContext();
        await rlsDb.Database.OpenConnectionAsync();
        await rlsDb.Database.ExecuteSqlAsync($"SELECT set_config('app.empresa_id', {empresaA.ToString()}, false)");
        (await rlsDb.Campanhas.IgnoreQueryFilters().Select(c => c.Id).ToListAsync()).Should().Equal(campanhaA.Id);
        (await rlsDb.CampanhaDestinatarios.IgnoreQueryFilters().Select(d => d.CampanhaId).ToListAsync()).Should().Equal(campanhaA.Id);

        var rls = await dbA.Database
            .SqlQueryRaw<bool>("""SELECT relrowsecurity AS "Value" FROM pg_class WHERE relname IN ('campanhas','campanha_destinatarios')""")
            .ToListAsync();
        rls.Should().HaveCount(2).And.OnlyContain(ativo => ativo);
    }

    [SkippableFact]
    public async Task DestinatarioUnicoPorCampanhaECancelamentoExcluiPendentes()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = Guid.NewGuid();
        var (campanha, cliente) = await SemearAsync(empresa, "Cancelar");

        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresa);
            db.CampanhaDestinatarios.Add(CampanhaDestinatario.Criar(campanha, cliente.Id));
            var duplicar = () => db.SaveChangesAsync();
            await duplicar.Should().ThrowAsync<DbUpdateException>();
        }

        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresa);
            var repo = new CampanhaRepository(db);
            var lida = await repo.ObterAsync(empresa, campanha.Id);
            var pendentes = await repo.ListarPendentesAsync(empresa, campanha.Id);
            pendentes.Should().ContainSingle();

            lida!.Cancelar(pendentes).Should().Be(1);
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresa);
            var destinatario = await db.CampanhaDestinatarios.AsNoTracking().SingleAsync(d => d.CampanhaId == campanha.Id);
            destinatario.Status.Should().Be(StatusCampanhaDestinatario.Excluido);
            destinatario.MotivoExclusao.Should().Be(MotivoExclusaoCampanha.Cancelada);
            (await db.Campanhas.AsNoTracking().SingleAsync(c => c.Id == campanha.Id)).Status.Should().Be(StatusCampanha.Cancelada);
        }
    }
}

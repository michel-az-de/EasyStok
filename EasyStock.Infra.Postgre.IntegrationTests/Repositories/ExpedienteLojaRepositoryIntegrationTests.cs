using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Enums.Storefront;
using EasyStock.Infra.Postgre.Repositories.Storefront;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// Expediente da loja (S40) em Postgres real: horários em jsonb voltam iguais, o checkout público
/// (sem tenant na sessão) lê pela mesma via do storefront, e a tabela nova tem a policy de RLS.
/// </summary>
public class ExpedienteLojaRepositoryIntegrationTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task GravaHorariosEControleELeituraPublicaEnxerga()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");

        var empresa = Empresa.Criar("Casa da Baba Expediente", "11111111000191");
        await using (var db = fixture.CreateDbContext())
        {
            db.Empresas.Add(empresa);
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresa.Id);
            var expediente = ExpedienteLoja.CriarPadrao(empresa.Id);
            expediente.DefinirHorarios([new HorarioFuncionamento(5, new TimeOnly(18, 0), new TimeOnly(2, 0))]);
            expediente.DefinirControle(ControleManualLoja.ForcarFechada, null, DateTime.UtcNow);
            await new ExpedienteLojaRepository(db).AddAsync(expediente);
            await db.SaveChangesAsync();
        }

        await using (var semTenant = fixture.CreateDbContext())
        {
            var lido = await new ExpedienteLojaRepository(semTenant).GetPublicoAsync(empresa.Id);

            lido.Should().NotBeNull("o checkout do site não tem JWT e precisa ver a pausa manual");
            lido!.ControleManual.Should().Be(ControleManualLoja.ForcarFechada);
            lido.Horarios.Should().ContainSingle()
                .Which.Should().Be(new HorarioFuncionamento(5, new TimeOnly(18, 0), new TimeOnly(2, 0)));
        }

        await using (var db = fixture.CreateDbContext())
        {
            (await db.Database
                .SqlQuery<int>($"SELECT COUNT(*)::int AS \"Value\" FROM pg_policies WHERE policyname = 'tenant_isolation' AND tablename = 'expedientes_loja'")
                .SingleAsync())
                .Should().Be(1, "tabela nova com EmpresaId precisa da policy tenant_isolation (ADR-0010)");
        }
    }
}

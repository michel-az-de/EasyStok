using EasyStock.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EasyStock.Infra.Postgre.IntegrationTests.Configurations;

public class EmpresaConfigurationTests(PostgreSqlDatabaseFixture fixture) : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task PhoneNumberIdUnico()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();

        var empresaA = Empresa.Criar("Empresa A", "11111111000191");
        empresaA.VincularWhatsApp("551199998888");
        await using (var db = fixture.CreateDbContext())
        {
            db.Set<Empresa>().Add(empresaA);
            await db.SaveChangesAsync();
        }

        var empresaB = Empresa.Criar("Empresa B", "22222222000192");
        empresaB.VincularWhatsApp("551199998888");

        await using var dbB = fixture.CreateDbContext();
        dbB.Set<Empresa>().Add(empresaB);
        var act = async () => await dbB.SaveChangesAsync();

        var ex = await act.Should().ThrowAsync<DbUpdateException>(
            "duas empresas nao podem apontar para o mesmo phone_number_id da Meta");
        ex.Which.InnerException.Should().BeOfType<PostgresException>()
          .Which.SqlState.Should().Be("23505", "violacao do indice unico parcial");
    }

    [SkippableFact]
    public async Task DuasEmpresasSemNumeroNaoColidem()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();

        var empresaA = Empresa.Criar("Empresa A", "11111111000191");
        var empresaB = Empresa.Criar("Empresa B", "22222222000192");

        await using var db = fixture.CreateDbContext();
        db.Set<Empresa>().AddRange(empresaA, empresaB);
        var act = async () => await db.SaveChangesAsync();

        await act.Should().NotThrowAsync("o indice e parcial (WHERE ... IS NOT NULL): NULL nao colide com NULL");
    }
}

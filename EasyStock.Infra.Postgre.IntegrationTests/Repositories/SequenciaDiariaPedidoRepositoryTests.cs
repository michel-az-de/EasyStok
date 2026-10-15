using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// S53 (#1283): o número do dia sai de um upsert atômico. Chamadas simultâneas da mesma empresa e dia recebem
/// números distintos e seguidos; outro dia e outra empresa começam do 1; o RLS isola o contador.
/// </summary>
public class SequenciaDiariaPedidoRepositoryTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    private static readonly DateOnly Dia = new(2026, 10, 2);

    private async Task<Empresa> NovaEmpresaAsync(string nome, string cnpj)
    {
        var empresa = Empresa.Criar(nome, cnpj);
        await using var db = fixture.CreateDbContext();
        using var _ = db.UseRowLevelSecurityBypass();
        await db.Database.MigrateAsync();
        db.Empresas.Add(empresa);
        await db.SaveChangesAsync();
        return empresa;
    }

    private async Task<int> ProximoAsync(Guid empresaId, DateOnly dia)
    {
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresaId);
        return await new SequenciaDiariaPedidoRepository(db).ProximoAsync(empresaId, dia);
    }

    [SkippableFact]
    public async Task ChamadasSimultaneasNaoRepetemNumero()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = await NovaEmpresaAsync("Casa da Baba Sequencia", "11222333000181");

        var numeros = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => ProximoAsync(empresa.Id, Dia)));

        numeros.Should().OnlyHaveUniqueItems().And.BeEquivalentTo(Enumerable.Range(1, 20));
    }

    [SkippableFact]
    public async Task CadaDiaECadaEmpresaComecaDoUm()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var a = await NovaEmpresaAsync("Casa da Baba Dia A", "11222333000262");
        var b = await NovaEmpresaAsync("Casa da Baba Dia B", "11222333000343");

        (await ProximoAsync(a.Id, Dia)).Should().Be(1);
        (await ProximoAsync(a.Id, Dia)).Should().Be(2);
        (await ProximoAsync(a.Id, Dia.AddDays(1))).Should().Be(1, "dia novo, sequência nova");
        (await ProximoAsync(b.Id, Dia)).Should().Be(1, "outra empresa, sequência própria");
    }
}

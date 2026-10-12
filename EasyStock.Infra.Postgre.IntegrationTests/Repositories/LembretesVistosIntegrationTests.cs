using EasyStock.Application.UseCases.Atendimento.Lembretes;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories.Atendimento;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

public class LembretesVistosIntegrationTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableTheory]
    [InlineData(0)]
    [InlineData(501)]
    public async Task MarcarVistos_AlcancaTodosOsVencidosMesmoComMaisDe500LembretesFuturos(int quantidadeFuturos)
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var agora = new DateTime(2026, 10, 9, 15, 0, 0, DateTimeKind.Utc);
        var empresa = Empresa.Criar("Empresa marcar vistos", null);
        var outraEmpresa = Empresa.Criar("Outra empresa marcar vistos", null);
        var usuario = Guid.NewGuid();
        var vencidos = Enumerable.Range(0, 501)
            .Select(i => Lembrete.Manual(empresa.Id, $"Vencido {i}", agora, usuario, agora, paraUsuarioId: usuario))
            .ToArray();
        var futuros = Enumerable.Range(0, quantidadeFuturos)
            .Select(i => Lembrete.Manual(empresa.Id, $"Futuro {i}", agora.AddDays(1), usuario, agora))
            .ToArray();
        var concluido = Lembrete.Manual(empresa.Id, "Concluído", agora, usuario, agora);
        concluido.Concluir(agora);
        var visto = Lembrete.Manual(empresa.Id, "Já visto", agora, usuario, agora);
        visto.MarcarVisto(agora.AddMinutes(-1));
        var colega = Lembrete.Manual(empresa.Id, "Colega", agora, usuario, agora, paraUsuarioId: Guid.NewGuid());
        var externo = Lembrete.Manual(outraEmpresa.Id, "Outra empresa", agora, usuario, agora);
        await using (var db = fixture.CreateDbContext())
        {
            db.Empresas.AddRange(empresa, outraEmpresa);
            db.Lembretes.AddRange(vencidos);
            db.Lembretes.AddRange(futuros);
            db.Lembretes.AddRange(concluido, visto, colega, externo);
            await db.SaveChangesAsync();
        }

        var comandos = new List<string>();
        var options = new DbContextOptionsBuilder<EasyStockDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .LogTo(comandos.Add, [RelationalEventId.CommandExecuted])
            .Options;
        await using (var db = new EasyStockDbContext(options))
        {
            db.SetMobileTenantContext(empresa.Id);
            var useCase = new MarcarLembretesVistosUseCase(new LembreteRepository(db), new RelogioFixo(agora));
            (await useCase.ExecuteAsync(empresa.Id, usuario)).Should().Be(501);
            comandos.Should().ContainSingle("a quantidade de comandos não cresce com o número de lembretes");
            db.ChangeTracker.Entries().Should().BeEmpty("não é necessário carregar os lembretes para marcá-los");
            (await useCase.ExecuteAsync(empresa.Id, usuario)).Should().Be(0, "a marcação é idempotente");
        }

        await using (var db = fixture.CreateDbContext())
        {
            var gravados = await db.Lembretes.IgnoreQueryFilters()
                .Where(l => l.EmpresaId == empresa.Id || l.EmpresaId == outraEmpresa.Id).ToListAsync();
            gravados.Where(l => l.Texto.StartsWith("Vencido")).Should().HaveCount(501).And.OnlyContain(l => l.VistoEm == agora);
            gravados.Single(l => l.Id == visto.Id).VistoEm.Should().Be(agora.AddMinutes(-1));
            gravados.Where(l => l.Id != visto.Id && !l.Texto.StartsWith("Vencido"))
                .Should().OnlyContain(l => l.VistoEm == null);
        }
    }

    [SkippableFact]
    public async Task MarcarVistos_Concorrente_PreservaPrimeiroCarimboEContaUmaUnicaAlteracao()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var agora = new DateTime(2026, 10, 9, 15, 0, 0, DateTimeKind.Utc);
        var empresa = Empresa.Criar("Empresa vistos concorrentes", null);
        var usuario = Guid.NewGuid();
        var lembrete = Lembrete.Manual(empresa.Id, "Visto uma vez", agora, usuario, agora);
        await using (var seed = fixture.CreateDbContext())
        {
            seed.Empresas.Add(empresa);
            seed.Lembretes.Add(lembrete);
            await seed.SaveChangesAsync();
        }

        await using var primeiro = fixture.CreateDbContext();
        await using var segundo = fixture.CreateDbContext();
        primeiro.SetMobileTenantContext(empresa.Id);
        segundo.SetMobileTenantContext(empresa.Id);
        var resultados = await Task.WhenAll(
            new MarcarLembretesVistosUseCase(new LembreteRepository(primeiro), new RelogioFixo(agora)).ExecuteAsync(empresa.Id, usuario),
            new MarcarLembretesVistosUseCase(new LembreteRepository(segundo), new RelogioFixo(agora.AddSeconds(1))).ExecuteAsync(empresa.Id, usuario));

        resultados.Sum().Should().Be(1);
        var gravado = await primeiro.Lembretes.AsNoTracking().SingleAsync(l => l.Id == lembrete.Id);
        gravado.VistoEm.Should().Be(resultados[0] == 1 ? agora : agora.AddSeconds(1));
    }

    [SkippableFact]
    public async Task MarcarVistos_RlsImpedeAlterarOutraEmpresaMesmoComContextoEfDivergente()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var agora = new DateTime(2026, 10, 9, 15, 0, 0, DateTimeKind.Utc);
        var empresa = Empresa.Criar("Empresa vistos RLS", null);
        var outraEmpresa = Empresa.Criar("Outra empresa vistos RLS", null);
        var usuario = Guid.NewGuid();
        var proprio = Lembrete.Manual(empresa.Id, "Meu", agora, usuario, agora);
        var externo = Lembrete.Manual(outraEmpresa.Id, "Outra empresa", agora, usuario, agora);
        await using (var seed = fixture.CreateDbContext())
        {
            seed.Empresas.AddRange(empresa, outraEmpresa);
            seed.Lembretes.AddRange(proprio, externo);
            await seed.SaveChangesAsync();
        }

        await using (var db = fixture.CreateRlsClientDbContext())
        {
            await db.Database.OpenConnectionAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.empresa_id', {empresa.Id.ToString()}, false)");
            db.SetMobileTenantContext(outraEmpresa.Id);
            var useCase = new MarcarLembretesVistosUseCase(new LembreteRepository(db), new RelogioFixo(agora));
            (await useCase.ExecuteAsync(outraEmpresa.Id, usuario)).Should().Be(0);
            db.SetMobileTenantContext(empresa.Id);
            (await useCase.ExecuteAsync(empresa.Id, usuario)).Should().Be(1);
        }

        await using var verificacao = fixture.CreateDbContext();
        (await verificacao.Lembretes.IgnoreQueryFilters().SingleAsync(l => l.Id == externo.Id)).VistoEm.Should().BeNull();
        (await verificacao.Lembretes.IgnoreQueryFilters().SingleAsync(l => l.Id == proprio.Id)).VistoEm.Should().Be(agora);
    }

    private sealed class RelogioFixo(DateTime agora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(agora, TimeSpan.Zero);
    }
}

using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Operacao;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories.Operacao;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// Fila de impressão (S20) em Postgres real: polling por empresa na ordem de chegada, isolamento de
/// tenant (filtro + RLS), varredura cross-tenant do alerta e a migration subindo e descendo com a policy.
/// Fixture por classe: a migration é desfeita e refeita no último cenário.
/// </summary>
public class ImpressaoPendenteRepositoryIntegrationTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    private const string MigrationAnterior = "20260930132753_AddClienteTagNotaBloqueioPreferencias";
    private static readonly DateTime Agora = new(2026, 9, 30, 15, 0, 0, DateTimeKind.Utc);

    private static async Task<Pedido> CriarPedidoAsync(EasyStockDbContext db, Guid empresaId)
    {
        var empresa = Empresa.Criar($"Empresa {empresaId:N}", null);
        empresa.Id = empresaId;
        db.Empresas.Add(empresa);
        var pedido = Pedido.Criar(empresaId, origem: "whatsapp");
        db.Pedidos.Add(pedido);
        await db.SaveChangesAsync();
        return pedido;
    }

    [SkippableFact]
    public async Task PendentesDaEmpresaNaOrdemDeChegadaSemAsImpressas()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = Guid.NewGuid();
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresa);
        var pedido = await CriarPedidoAsync(db, empresa);
        var repo = new ImpressaoPendenteRepository(db);
        var segunda = ImpressaoPendente.CriarCanhoto(empresa, null, pedido.Id, Agora.AddMinutes(1));
        var primeira = ImpressaoPendente.CriarCanhoto(empresa, null, pedido.Id, Agora);
        var impressa = ImpressaoPendente.CriarCanhoto(empresa, null, pedido.Id, Agora.AddMinutes(-1));
        impressa.MarcarImpressa(Agora);
        await repo.AddAsync(segunda);
        await repo.AddAsync(primeira);
        await repo.AddAsync(impressa);
        await db.SaveChangesAsync();

        var pendentes = await repo.ListarPendentesAsync(empresa, 10);

        pendentes.Select(p => p.Id).Should().Equal(primeira.Id, segunda.Id);
        (await repo.GetByIdAsync(empresa, impressa.Id))!.ImpressaEm.Should().Be(Agora);
    }

    [SkippableFact]
    public async Task IsolamentoDeTenant()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresaA = Guid.NewGuid();
        var empresaB = Guid.NewGuid();
        Guid impressaoB;
        await using (var dbB = fixture.CreateDbContext())
        {
            dbB.SetMobileTenantContext(empresaB);
            var pedido = await CriarPedidoAsync(dbB, empresaB);
            var i = ImpressaoPendente.CriarCanhoto(empresaB, null, pedido.Id, Agora);
            dbB.ImpressoesPendentes.Add(i);
            await dbB.SaveChangesAsync();
            impressaoB = i.Id;
        }

        await using var dbA = fixture.CreateDbContext();
        dbA.SetMobileTenantContext(empresaA);
        var repo = new ImpressaoPendenteRepository(dbA);
        (await repo.ListarPendentesAsync(empresaA, 50)).Should().NotContain(p => p.Id == impressaoB);
        (await repo.GetByIdAsync(empresaA, impressaoB)).Should().BeNull("impressão de outra empresa é invisível");
        (await dbA.ImpressoesPendentes.CountAsync(i => i.Id == impressaoB)).Should().Be(0, "RLS esconde a linha");
    }

    [SkippableFact]
    public async Task AtrasadasCrossTenantDentroDaJanela()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = Guid.NewGuid();
        Guid atrasada, recente, antiga;
        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresa);
            var pedido = await CriarPedidoAsync(db, empresa);
            var a = ImpressaoPendente.CriarCanhoto(empresa, null, pedido.Id, Agora.AddMinutes(-5));
            var r = ImpressaoPendente.CriarCanhoto(empresa, null, pedido.Id, Agora.AddMinutes(-1));
            var v = ImpressaoPendente.CriarCanhoto(empresa, null, pedido.Id, Agora.AddHours(-13));
            db.ImpressoesPendentes.AddRange(a, r, v);
            await db.SaveChangesAsync();
            (atrasada, recente, antiga) = (a.Id, r.Id, v.Id);
        }

        await using var semTenant = fixture.CreateDbContext();
        var lista = await new ImpressaoPendenteRepository(semTenant)
            .ListarAtrasadasAsync(Agora.AddHours(-12), Agora.AddMinutes(-3), 50);

        lista.Should().Contain(x => x.ImpressaoId == atrasada && x.EmpresaId == empresa, "o job roda sem tenant");
        lista.Select(x => x.ImpressaoId).Should().NotContain(recente).And.NotContain(antiga);
    }

    [SkippableFact]
    public async Task Migration_SobeEDesceLimpaComRls()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");

        await using var db = fixture.CreateDbContext();
        var migrator = db.GetService<IMigrator>();

        (await ContarTabelaAsync(db)).Should().Be(1);
        (await ContarPolicyAsync(db)).Should().Be(1, "a tabela nova precisa da policy tenant_isolation (ADR-0010)");

        await migrator.MigrateAsync(MigrationAnterior);
        (await ContarTabelaAsync(db)).Should().Be(0, "Down remove a tabela");

        await migrator.MigrateAsync();
        (await ContarTabelaAsync(db)).Should().Be(1);
        (await ContarPolicyAsync(db)).Should().Be(1);
    }

    private static Task<int> ContarTabelaAsync(DbContext db) =>
        db.Database
            .SqlQuery<int>($"SELECT COUNT(*)::int AS \"Value\" FROM information_schema.tables WHERE table_schema = current_schema() AND table_name = 'impressoes_pendentes'")
            .SingleAsync();

    private static Task<int> ContarPolicyAsync(DbContext db) =>
        db.Database
            .SqlQuery<int>($"SELECT COUNT(*)::int AS \"Value\" FROM pg_policies WHERE policyname = 'tenant_isolation' AND tablename = 'impressoes_pendentes'")
            .SingleAsync();
}

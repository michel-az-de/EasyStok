using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Data.Interceptors;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Admin;

/// <summary>
/// Gate de produção do MRR do dashboard Admin (F2 do ADR-0037 adendo, issue #754) contra
/// Postgres REAL (Testcontainers). Prova o que quebraria em prod: <c>AssinaturasEmpresa</c> é
/// protegida por RLS e a agregação cross-tenant do SuperAdmin só a enxerga porque
/// <c>AdminDashboardQueries</c> chama <c>UseRowLevelSecurityBypass</c> e o interceptor emite
/// <c>SET app.bypass_rls</c>. Sem esse bypass, sob a role NOBYPASSRLS (espelha
/// <c>easystok_user</c> de prod) a policy zera as linhas e <c>ReceitaMensalEstimada</c> volta 0
/// (o sintoma do bug). Na CI o Postgres roda superuser (ignora RLS), então o bug é invisível
/// lá — só este teste de runtime o pega.
/// </summary>
public class AdminDashboardQueriesPostgresTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task Soma_MRR_cross_tenant_via_bypass_de_RLS()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");

        var now = DateTime.UtcNow;
        var alpha = Guid.NewGuid();   // Ativa, plano R$100
        var bravo = Guid.NewGuid();   // Ativa, plano R$200

        await SeedAsync(now, alpha, bravo);

        // Contexto de consulta: SuperAdmin (abre o filtro EF) + interceptor (pra o bypass de RLS
        // chamado dentro da query realmente emitir SET app.bypass_rls), sob a role NOBYPASSRLS.
        var superAdmin = Substitute.For<ICurrentUserAccessor>();
        superAdmin.IsAuthenticated.Returns(true);
        superAdmin.Nivel.Returns(NivelAcesso.SuperAdmin);

        var options = new DbContextOptionsBuilder<EasyStockDbContext>()
            .UseNpgsql(fixture.RlsClientConnectionString)
            .AddInterceptors(new SetTenantOnConnectionInterceptor())
            .Options;
        await using var ctx = new EasyStockDbContext(options, superAdmin);

        var data = await new AdminDashboardQueries(ctx, new AssinaturaEmpresaRepository(ctx))
            .ObterAsync(now);

        // <- prova o bypass: soma os 2 tenants cross-tenant. Sem o UseRowLevelSecurityBypass do
        // AdminDashboardQueries, a RLS zeraria AssinaturasEmpresa e isto voltaria 0.
        data.ReceitaMensalEstimada.Should().Be(300m);   // 100 + 200
        data.TenantsAtivos.Should().Be(2);
        // TotalTenants é só controle de seed: Empresas não tem coluna EmpresaId, logo fica FORA
        // da RLS (migration AddRowLevelSecurity só protege tabelas com essa coluna) — passaria
        // mesmo sem o bypass. As 2 asserções acima é que provam o fix.
        data.TotalTenants.Should().Be(2);
    }

    private async Task SeedAsync(DateTime now, Guid alpha, Guid bravo)
    {
        await fixture.ResetDatabaseAsync();

        await using var seed = fixture.CreateRlsClientDbContext();
        await seed.Database.OpenConnectionAsync();
        await seed.Database.ExecuteSqlRawAsync("SET app.bypass_rls = 'true'");

        var pAlpha = new Plano { Id = Guid.NewGuid(), Nome = "Starter", PrecoMensal = 100m, Ativo = true, CriadoEm = now };
        var pBravo = new Plano { Id = Guid.NewGuid(), Nome = "Plus", PrecoMensal = 200m, Ativo = true, CriadoEm = now };
        seed.Planos.AddRange(pAlpha, pBravo);

        seed.Empresas.AddRange(Empresa(alpha, "Alpha"), Empresa(bravo, "Bravo"));
        seed.AssinaturasEmpresa.AddRange(
            Assinatura(alpha, pAlpha.Id, now),
            Assinatura(bravo, pBravo.Id, now));

        await seed.SaveChangesAsync();
    }

    private static Empresa Empresa(Guid id, string nome) => new()
    {
        Id = id,
        Nome = nome,
        Documento = id.ToString("N")[..11],
        CriadoEm = DateTime.UtcNow,
        AlteradoEm = DateTime.UtcNow,
    };

    private static AssinaturaEmpresa Assinatura(Guid empresaId, Guid planoId, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        EmpresaId = empresaId,
        PlanoId = planoId,
        Status = StatusAssinatura.Ativa,
        DataInicio = now.AddDays(-30),
        CriadoEm = now.AddDays(-30),
        AlteradoEm = now,
    };
}

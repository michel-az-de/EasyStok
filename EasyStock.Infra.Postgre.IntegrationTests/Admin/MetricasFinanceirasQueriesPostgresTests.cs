using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Data.Interceptors;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Admin;

/// <summary>
/// Gate de produção das métricas financeiras do Admin (issue 762) contra Postgres REAL sob
/// role NOBYPASSRLS (espelha <c>easystok_user</c> de prod). Prova os DOIS lados do bypass
/// CONDICIONAL de <c>MetricasFinanceirasQueries</c>:
/// (a) SuperAdmin cross-tenant enxerga todos os tenants (sem o bypass, a policy zeraria
///     assinaturas e faturas — o repo usa só IgnoreQueryFilters, que não desliga a RLS);
/// (b) admin operacional NÃO ganha bypass: mesmo pedindo <c>empresaId=null</c> (o que o
///     controller nunca faz), a RLS confina ao tenant da sessão — defense-in-depth contra
///     regressão do filtro explícito. Invisível na CI (superuser ignora RLS).
/// </summary>
public class MetricasFinanceirasQueriesPostgresTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    private static readonly Guid Alpha = Guid.NewGuid();   // plano R$100, fatura paga R$50
    private static readonly Guid Bravo = Guid.NewGuid();   // plano R$200, fatura paga R$70

    [SkippableFact]
    public async Task SuperAdmin_cross_tenant_agrega_todos_os_tenants_via_bypass()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await SeedAsync();

        await using var ctx = CriarContexto(nivel: NivelAcesso.SuperAdmin, empresaSessao: Guid.Empty);
        var r = await CriarQueries(ctx).ComputarAsync(dias: 30, empresaId: null);

        // <- prova o bypass: sem ele, RLS zera AssinaturasEmpresa e Faturas e tudo volta 0.
        r.Mrr.Should().Be(300m);                 // 100 + 200
        r.ReceitaPeriodo.Should().Be(120m);      // 50 + 70 (faturas Pagas dos 2 tenants)
        r.FaturasPagasPeriodo.Should().Be(2);
        r.AssinaturasAtivas.Should().Be(2);
    }

    [SkippableFact]
    public async Task Operacional_nao_ganha_bypass_e_fica_confinado_ao_proprio_tenant()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await SeedAsync();

        await using var ctx = CriarContexto(nivel: NivelAcesso.Admin, empresaSessao: Alpha);
        var queries = CriarQueries(ctx);

        // Caminho normal do controller: operacional sempre chega com o próprio tenant.
        var proprio = await queries.ComputarAsync(dias: 30, empresaId: Alpha);
        proprio.Mrr.Should().Be(100m);
        proprio.ReceitaPeriodo.Should().Be(50m);

        // Defense-in-depth: mesmo se o filtro explícito regredir (empresaId=null), o bypass
        // NÃO abre para não-SuperAdmin e a RLS confina ao tenant da sessão — nada de 300/120.
        var semFiltro = await queries.ComputarAsync(dias: 30, empresaId: null);
        semFiltro.Mrr.Should().Be(100m);
        semFiltro.ReceitaPeriodo.Should().Be(50m);
        semFiltro.AssinaturasAtivas.Should().Be(1);
    }

    private static MetricasFinanceirasQueries CriarQueries(EasyStockDbContext ctx)
        => new(ctx, new FaturaRepository(ctx), new AssinaturaEmpresaRepository(ctx));

    private EasyStockDbContext CriarContexto(NivelAcesso nivel, Guid empresaSessao)
    {
        var user = Substitute.For<ICurrentUserAccessor>();
        user.IsAuthenticated.Returns(true);
        user.Nivel.Returns(nivel);
        user.EmpresaId.Returns(empresaSessao);

        var options = new DbContextOptionsBuilder<EasyStockDbContext>()
            .UseNpgsql(fixture.RlsClientConnectionString)
            .AddInterceptors(new SetTenantOnConnectionInterceptor())
            .Options;
        return new EasyStockDbContext(options, user);
    }

    private async Task SeedAsync()
    {
        await fixture.ResetDatabaseAsync();

        await using var seed = fixture.CreateRlsClientDbContext();
        await seed.Database.OpenConnectionAsync();
        await seed.Database.ExecuteSqlRawAsync("SET app.bypass_rls = 'true'");

        var now = DateTime.UtcNow;
        var pAlpha = new Plano { Id = Guid.NewGuid(), Nome = "Starter", PrecoMensal = 100m, Ativo = true, CriadoEm = now };
        var pBravo = new Plano { Id = Guid.NewGuid(), Nome = "Plus", PrecoMensal = 200m, Ativo = true, CriadoEm = now };
        seed.Planos.AddRange(pAlpha, pBravo);

        seed.Empresas.AddRange(Empresa(Alpha, "Alpha"), Empresa(Bravo, "Bravo"));
        seed.AssinaturasEmpresa.AddRange(
            Assinatura(Alpha, pAlpha.Id, now),
            Assinatura(Bravo, pBravo.Id, now));
        seed.Faturas.AddRange(
            FaturaPaga(Alpha, total: 50m, now),
            FaturaPaga(Bravo, total: 70m, now));

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

    private static Fatura FaturaPaga(Guid empresaId, decimal total, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        EmpresaId = empresaId,
        Numero = $"2026-{Math.Abs(empresaId.GetHashCode()) % 1000000:D6}",
        DadosFaturado = new DadosFaturado("Cliente Teste"),
        DadosEmissor = new DadosEmissor("EasyStok"),
        Origem = OrigemFatura.Assinatura,
        Status = StatusFatura.Paga,
        DataEmissao = now.AddDays(-1),
        DataVencimento = now.AddDays(5),
        DataPagamentoTotal = now,
        SubTotal = total,
        Total = total,
        CriadoEm = now.AddDays(-1),
        AlteradoEm = now,
    };
}

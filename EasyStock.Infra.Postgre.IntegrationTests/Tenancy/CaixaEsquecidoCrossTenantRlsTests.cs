using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Concurrency;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Data.Interceptors;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Tenancy;

/// <summary>
/// KEYSTONE do job de "caixa esquecido" (issue #641, B-BLOCKER-1).
///
/// O job varre <c>movimentos_caixa</c> CROSS-TENANT sob um advisory lock para achar aberturas
/// pendentes de dias anteriores. A defesa em profundidade exige DESLIGAR as duas camadas: o
/// filtro EF (<c>IgnoreQueryFilters</c>) E a policy RLS do Postgres (<c>UseRowLevelSecurityBypass</c>).
///
/// A armadilha (B-BLOCKER-1): <see cref="SetTenantOnConnectionInterceptor"/> emite
/// <c>SET app.bypass_rls</c> só em <c>ConnectionOpened</c>, lendo a flag NAQUELE instante.
/// <see cref="PostgresAdvisoryLock.TentarExecutarAsync"/> abre a conexão ANTES de rodar o
/// <c>action</c>. Logo, ligar o bypass DENTRO do action (depois do open) não re-emite o SET
/// e a policy zera as linhas — apagão silencioso (0 aberturas, nenhuma notificação, sem erro).
///
/// Prova: ligar o bypass ANTES de o advisory lock abrir a conexão funciona; ligar depois não.
/// Roda como <c>rls_test_client</c> (NOSUPERUSER); o superuser <c>postgres</c> ignoraria o RLS
/// e não provaria nada.
/// </summary>
public class CaixaEsquecidoCrossTenantRlsTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    private const long LockKey = 0x4361_6978_4573_7100L; // "CaixEsq" — chave de teste

    [SkippableFact]
    public async Task Bypass_ligado_ANTES_do_advisory_lock_ve_aberturas_de_todos_os_tenants()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await SeedDuasEmpresasComAberturaAsync();

        await using var db = BuildJobCtx();
        using (db.UseRowLevelSecurityBypass()) // ← ANTES de qualquer conexão abrir
        {
            var lockUtil = new PostgresAdvisoryLock(db, NullLogger<PostgresAdvisoryLock>.Instance);
            var count = -1;
            var ran = await lockUtil.TentarExecutarAsync(LockKey, async ct =>
            {
                count = await db.MovimentosCaixa.IgnoreQueryFilters()
                    .CountAsync(m => m.Tipo == "abertura", ct);
            }, CancellationToken.None);

            ran.Should().BeTrue();
            count.Should().Be(2,
                "com o bypass ligado antes do open, a varredura cross-tenant enxerga as 2 aberturas");
        }
    }

    [SkippableFact]
    public async Task Bypass_ligado_DEPOIS_do_open_NAO_ve_nada_documenta_a_armadilha()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await SeedDuasEmpresasComAberturaAsync();

        await using var db = BuildJobCtx();
        var lockUtil = new PostgresAdvisoryLock(db, NullLogger<PostgresAdvisoryLock>.Instance);
        var count = -1;
        await lockUtil.TentarExecutarAsync(LockKey, async ct =>
        {
            using var _ = db.UseRowLevelSecurityBypass(); // ← DEPOIS do open: SET não é re-emitido
            count = await db.MovimentosCaixa.IgnoreQueryFilters()
                .CountAsync(m => m.Tipo == "abertura", ct);
        }, CancellationToken.None);

        count.Should().Be(0,
            "bypass ligado com a conexão já aberta não re-emite SET app.bypass_rls → RLS zera (apagão silencioso)");
    }

    // DbContext como o job: rls_test_client (sujeito a RLS) + interceptor + usuário não-autenticado
    // (CurrentTenantId = Guid.Empty), de modo que só o bypass abre a varredura cross-tenant.
    private EasyStockDbContext BuildJobCtx()
    {
        var jobUser = Substitute.For<ICurrentUserAccessor>(); // não autenticado
        var options = new DbContextOptionsBuilder<EasyStockDbContext>()
            .UseNpgsql(fixture.RlsClientConnectionString)
            .AddInterceptors(new SetTenantOnConnectionInterceptor())
            .Options;
        return new EasyStockDbContext(options, jobUser);
    }

    private async Task SeedDuasEmpresasComAberturaAsync()
    {
        await fixture.ResetDatabaseAsync();

        await using var seed = fixture.CreateRlsClientDbContext();
        await seed.Database.OpenConnectionAsync();
        await seed.Database.ExecuteSqlRawAsync("SET app.bypass_rls = 'true'");

        var a = new Empresa { Id = Guid.NewGuid(), Nome = "Empresa A", Documento = "11111111111", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow };
        var b = new Empresa { Id = Guid.NewGuid(), Nome = "Empresa B", Documento = "22222222222", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow };
        seed.Set<Empresa>().AddRange(a, b);

        var ontem = DateTime.UtcNow.AddDays(-1);
        seed.MovimentosCaixa.Add(MovimentoCaixa.Criar(a.Id, "abertura", 100m, ontem));
        seed.MovimentosCaixa.Add(MovimentoCaixa.Criar(b.Id, "abertura", 200m, ontem));
        await seed.SaveChangesAsync();
    }
}

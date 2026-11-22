using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Integration;
using EasyStock.Domain.Reporting;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.DependencyInjection;
using EasyStock.Infra.Postgre.Hosting;
using EasyStock.Infra.Postgre.Repositories.Reporting;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Tenancy;

/// <summary>
/// #3108: os workers do outbox de integração, dos relatórios e da retenção de auditoria rodam sem usuário
/// (<c>CurrentTenantId</c> vazio) e atravessam empresas por natureza. Com a policy <c>tenant_isolation</c> em
/// <c>outbox_evento_integracao</c>, <c>report_runs</c> e <c>entity_alteracoes</c>, sem bypass eles leem zero
/// linhas e param sem erro. O código precisa estar pronto antes da migração: este teste liga a policy no
/// banco da classe e roda o DI de produção como <c>rls_test_client</c> (NOSUPERUSER, NOBYPASSRLS).
/// </summary>
public sealed class WorkersSobRlsTests(PostgreSqlDatabaseFixture fixture) : IClassFixture<PostgreSqlDatabaseFixture>
{
    private const string Tipo = "teste.3108";

    [SkippableFact]
    public async Task Outbox_despacha_os_eventos_de_duas_empresas_com_o_tenant_de_cada_evento()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        await PrepararAsync();
        var (a, b) = await SemearEmpresasAsync();
        // Intercalados: o lote divide o DbContext e o tenant do evento anterior não pode sobrar para o seguinte.
        var eventos = new[] { Evento(a, 0), Evento(b, 1), Evento(a, 2) };
        await SemearAsync(db => db.Set<OutboxEventoIntegracao>().AddRange(eventos));

        await using var provider = BuildProvider();
        using (var scope = provider.CreateScope())
        {
            var despachados = await scope.ServiceProvider.GetRequiredService<IIntegrationEventDispatcher>()
                .ExecutarRodadaAsync(10, CancellationToken.None);
            despachados.Should().Be(3, "o lote é lido de todas as empresas");
        }

        var vistos = provider.GetRequiredService<TenantsVistos>();
        vistos.Should().Equal(eventos.Select(e => (e.EmpresaId, e.EmpresaId, "false")),
            "cada handler roda com o tenant do próprio evento e sem o bypass da leitura do lote");
        await using var leitura = fixture.CreateDbContext();
        var status = await leitura.Set<OutboxEventoIntegracao>().IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.TipoEvento == Tipo).Select(e => e.Status).ToListAsync();
        status.Should().OnlyContain(s => s == StatusOutboxIntegracao.Enviado, "o estado final de cada evento precisa ser gravado");
    }

    [SkippableFact]
    public async Task Outbox_reclama_o_evento_preso_em_envio_de_qualquer_empresa()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        await PrepararAsync();
        var (a, _) = await SemearEmpresasAsync();
        var preso = Evento(a, 0);
        preso.Status = StatusOutboxIntegracao.EmEnvio;
        preso.ProximaTentativaEm = DateTime.UtcNow.AddHours(-1);
        await SemearAsync(db => db.Set<OutboxEventoIntegracao>().Add(preso));

        await using var provider = BuildProvider();
        using (var scope = provider.CreateScope())
            (await scope.ServiceProvider.GetRequiredService<IOutboxEventoIntegracaoRepository>()
                .ReclamarEmEnvioExpiradosAsync(10)).Should().Be(1);

        await using var leitura = fixture.CreateDbContext();
        (await leitura.Set<OutboxEventoIntegracao>().IgnoreQueryFilters().AsNoTracking().SingleAsync(e => e.Id == preso.Id))
            .Status.Should().Be(StatusOutboxIntegracao.Pendente);
    }

    [SkippableFact]
    public async Task Retencao_apaga_a_auditoria_antiga_de_todas_as_empresas()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        await PrepararAsync();
        var (a, b) = await SemearEmpresasAsync();
        var antigas = new[] { a, b }.Select(empresa => new EntityAlteracao
        {
            Id = Guid.NewGuid(), EmpresaId = empresa, TipoEntidade = "Teste3108", EntidadeId = Guid.NewGuid(),
            Acao = "Update", AlteradoEm = DateTime.UtcNow.AddDays(-4000),
        }).ToList();
        await SemearAsync(db => db.Set<EntityAlteracao>().AddRange(antigas));

        await using var provider = BuildProvider();
        await new EntityAlteracaoRetentionService(provider, NullLogger<EntityAlteracaoRetentionService>.Instance)
            .RodadaAsync(CancellationToken.None);

        await using var leitura = fixture.CreateDbContext();
        var ids = antigas.Select(x => x.Id).ToList();
        (await leitura.Set<EntityAlteracao>().IgnoreQueryFilters().CountAsync(x => ids.Contains(x.Id)))
            .Should().Be(0, "a retenção atravessa empresas; sem bypass apagaria zero linhas e pararia em silêncio");
    }

    [SkippableFact]
    public async Task Relatorios_o_runner_escolhe_e_inicia_runs_de_qualquer_empresa()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        await PrepararAsync();
        var runs = new[] { Run(), Run() };
        await SemearAsync(db => db.ReportRuns.AddRange(runs));

        await using var provider = BuildProvider();
        using (var scope = provider.CreateScope())
        {
            var escolhidas = await Repo(scope).PickNextBatchAsync(10, CancellationToken.None);
            escolhidas.Should().Contain(runs.Select(r => r.Id));
        }

        // Contexto sem retry, como os outros testes de relatório: com EnableRetryOnFailure a transação
        // manual da reserva lança antes de chegar à RLS (#3141).
        await using (var db = ContextoSemRetry())
        {
            var iniciada = await new ReportRunRepository(db).TryStartAsync(runs[0].Id, TimeSpan.FromMinutes(5), CancellationToken.None);
            iniciada.Should().NotBeNull("o start é a reserva da run, antes de existir o tenant da execução");
            iniciada!.Status.Should().Be(ReportStatus.Running);
            db.BypassRowLevelSecurity.Should().BeFalse("o bypass acaba junto com a reserva");
        }

        await using var leitura = fixture.CreateDbContext();
        (await leitura.ReportRuns.AsNoTracking().SingleAsync(r => r.Id == runs[0].Id)).Status.Should().Be(ReportStatus.Running);
    }

    [SkippableFact]
    public async Task Relatorios_o_watchdog_recicla_runs_de_qualquer_empresa()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        await PrepararAsync();
        var vencida = Run();
        vencida.TryStart(TimeSpan.FromMinutes(5));
        await SemearAsync(db => db.ReportRuns.Add(vencida));
        var passado = DateTimeOffset.UtcNow.AddDays(-1);
        await using (var ajuste = fixture.CreateDbContext())
            await ajuste.Database.ExecuteSqlAsync($"UPDATE public.report_runs SET lease_expires_at = {passado} WHERE id = {vencida.Id}");

        await using var provider = BuildProvider();
        using (var scope = provider.CreateScope())
            (await Repo(scope).ReclaimExpiredLeasesAsync(CancellationToken.None)).Total.Should().Be(1);

        await using var leitura = fixture.CreateDbContext();
        (await leitura.ReportRuns.AsNoTracking().SingleAsync(r => r.Id == vencida.Id)).Status.Should().Be(ReportStatus.Pending);
    }

    private EasyStockDbContext ContextoSemRetry() => new(
        new DbContextOptionsBuilder<EasyStockDbContext>().UseNpgsql(fixture.RlsClientConnectionString)
            .AddInterceptors(new EasyStock.Infra.Postgre.Data.Interceptors.SetTenantOnConnectionInterceptor()).Options,
        Substitute.For<ICurrentUserAccessor>());

    private static ReportRunRepository Repo(IServiceScope scope) =>
        new(scope.ServiceProvider.GetRequiredService<EasyStockDbContext>());

    private static ReportRun Run() => ReportRun.Enqueue(Guid.NewGuid(), Guid.NewGuid(), "vendas.por-periodo",
        ReportCategoria.Vendas, ReportContexto.Tenant, "{}", Guid.NewGuid().ToString("N"), ReportFormat.Csv, "1.0.0",
        TimeSpan.FromDays(7));

    private static OutboxEventoIntegracao Evento(Guid empresa, int minuto)
    {
        var e = OutboxEventoIntegracao.Criar(empresa, Tipo, "Pedido", Guid.NewGuid(), "{}");
        e.CriadoEm = new DateTime(2000, 1, 1, 0, minuto, 0, DateTimeKind.Utc);
        e.ProximaTentativaEm = e.CriadoEm;
        return e;
    }

    private async Task<(Guid A, Guid B)> SemearEmpresasAsync()
    {
        var a = Empresa.Criar("RLS 3108 A", null);
        var b = Empresa.Criar("RLS 3108 B", null);
        await SemearAsync(db => db.Empresas.AddRange(a, b));
        return (a.Id, b.Id);
    }

    /// <summary>Superusuário: semeia sem depender do código sob teste.</summary>
    private async Task SemearAsync(Action<EasyStockDbContext> semear)
    {
        await using var seed = fixture.CreateDbContext();
        semear(seed);
        await seed.SaveChangesAsync();
    }

    /// <summary>
    /// A policy que a migração da #3108 liga nestas três tabelas. Idempotente; o FORCE fica fora porque só
    /// obriga o dono e o <c>rls_test_client</c> não é dono.
    /// </summary>
    private async Task PrepararAsync()
    {
        await fixture.ResetDatabaseAsync(); // cada teste parte do banco limpo: o lote do outbox é global
        await using var db = fixture.CreateDbContext();
        await db.Database.ExecuteSqlRawAsync("""
            DO $rls$
            DECLARE r record;
            BEGIN
              FOR r IN SELECT * FROM (VALUES ('outbox_evento_integracao', 'empresa_id'), ('report_runs', 'empresa_id'),
                                             ('entity_alteracoes', 'EmpresaId')) AS v(tabela, coluna) LOOP
                EXECUTE format('ALTER TABLE %I ENABLE ROW LEVEL SECURITY', r.tabela);
                EXECUTE format('DROP POLICY IF EXISTS tenant_isolation ON %I', r.tabela);
                EXECUTE format($pol$CREATE POLICY tenant_isolation ON %I
                    USING (current_setting('app.bypass_rls', true) = 'true'
                           OR %I = NULLIF(current_setting('app.empresa_id', true), '')::uuid)
                    WITH CHECK (current_setting('app.bypass_rls', true) = 'true'
                           OR %I = NULLIF(current_setting('app.empresa_id', true), '')::uuid)$pol$, r.tabela, r.coluna, r.coluna);
              END LOOP;
            END
            $rls$;
            """);
    }

    /// <summary>DI de produção, sem usuário (como no Worker), no login sujeito às policies.</summary>
    private ServiceProvider BuildProvider()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection([]).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddMemoryCache();
        services.AddHttpContextAccessor();
        services.AddSingleton(Substitute.For<ICurrentUserAccessor>());
        services.AddSingleton(Substitute.For<ICacheService>());
        services.AddEasyStockPostgreInfrastructure(fixture.RlsClientConnectionString, config);
        services.AddSingleton<TenantsVistos>();
        services.AddKeyedScoped<IIntegrationEventHandler, HandlerQueAnotaOTenant>(Tipo);
        return services.BuildServiceProvider();
    }

    private sealed class TenantsVistos : List<(Guid Evento, Guid Tenant, string Bypass)>;

    /// <summary>Handler que não fixa tenant, como os de log: herda o que o dispatcher deixou no escopo.</summary>
    private sealed class HandlerQueAnotaOTenant(EasyStockDbContext db, TenantsVistos vistos) : IIntegrationEventHandler
    {
        public string TipoEvento => Tipo;

        public async Task HandleAsync(OutboxEventoIntegracao evento, CancellationToken ct)
        {
            // O que a sessão do banco enxerga, não só a flag do contexto.
            var bypass = await db.Database
                .SqlQueryRaw<string>("SELECT current_setting('app.bypass_rls', true) AS \"Value\"").FirstAsync(ct);
            vistos.Add((evento.EmpresaId, db.CurrentTenantId, bypass));
        }
    }
}

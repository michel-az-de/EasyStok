using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Data.Interceptors;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace EasyStock.Infra.Postgre.IntegrationTests.Notifications;

/// <summary>
/// N1 sob o papel de produção: <c>rls_test_client</c> é NOSUPERUSER e NOBYPASSRLS (ADR-0010), então a policy
/// <c>tenant_isolation</c> devolve 0 linhas, sem erro, a quem não fixa tenant nem liga o bypass. Cada teste prova uma
/// parte do motor com esse papel; as sementes e as leituras de conferência vão pelo superusuário.
/// </summary>
public class MotorNotificacoesRlsTests(PostgreSqlDatabaseFixture fixture) : IClassFixture<PostgreSqlDatabaseFixture>
{
    private readonly MotorNotificacoesSuporte _s = new(fixture);

    // ----- catálogo global -----

    [SkippableFact]
    public async Task Catalogo_global_e_legivel_no_escopo_da_empresa_e_imutavel_por_ela()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = await _s.SemearEmpresaAsync();
        await _s.SemearCatalogoGlobalAsync(TipoEventoNotificacao.CaixaAbertoEsquecido, CanalNotificacao.InApp);
        await using (var seed = fixture.CreateDbContext())
        {
            // Por canal (Sms) e removido no fim: um kill switch global sem canal pararia os outros testes da classe.
            seed.NotifBloqueios.Add(BloqueioNotificacao.Criar("Teste do catálogo global", "teste", canal: CanalNotificacao.Sms));
            await seed.SaveChangesAsync();
        }

        await using var db = NovoDbDaEmpresaComRls(empresa);

        // Leitura: as quatro tabelas do catálogo expõem a linha global ao tenant.
        (await db.NotifRotinas.IgnoreQueryFilters().CountAsync(r => r.EmpresaId == null)).Should().BeGreaterThan(0);
        (await db.NotifTemplates.IgnoreQueryFilters().CountAsync(t => t.EmpresaId == null)).Should().BeGreaterThan(0);
        (await db.NotifConfiguracoesCanal.IgnoreQueryFilters().CountAsync(c => c.EmpresaId == null)).Should().BeGreaterThan(0);
        (await db.NotifBloqueios.IgnoreQueryFilters().CountAsync(b => b.EmpresaId == null)).Should().BeGreaterThan(0);

        // Imutável pela empresa: UPDATE e DELETE afetam 0 linhas (a linha global não passa no USING da tenant_isolation).
        (await db.NotifRotinas.IgnoreQueryFilters().Where(r => r.EmpresaId == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Nome, "alterada pela empresa"))).Should().Be(0);
        (await db.NotifTemplates.IgnoreQueryFilters().Where(t => t.EmpresaId == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Nome, "alterado pela empresa"))).Should().Be(0);
        (await db.NotifConfiguracoesCanal.IgnoreQueryFilters().Where(c => c.EmpresaId == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ProviderAtivo, "alterado"))).Should().Be(0);
        (await db.NotifBloqueios.IgnoreQueryFilters().Where(b => b.EmpresaId == null)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.Motivo, "alterado"))).Should().Be(0);
        (await db.NotifRotinas.IgnoreQueryFilters().Where(r => r.EmpresaId == null).ExecuteDeleteAsync()).Should().Be(0);
        (await db.NotifBloqueios.IgnoreQueryFilters().Where(b => b.EmpresaId == null).ExecuteDeleteAsync()).Should().Be(0);

        // INSERT global: a policy de leitura não concede escrita, então o WITH CHECK da tenant_isolation recusa (42501).
        db.NotifBloqueios.Add(BloqueioNotificacao.Criar("kill switch global forjado pela empresa", "empresa"));
        var falha = await FluentActions.Awaiting(() => db.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
        falha.Which.InnerException.Should().BeOfType<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);

        // A linha global continua intacta, lida pelo superusuário.
        await using var conferencia = fixture.CreateDbContext();
        (await conferencia.NotifRotinas.IgnoreQueryFilters().AnyAsync(r => r.EmpresaId == null && r.Nome == "Rotina global de teste"))
            .Should().BeTrue();
        await conferencia.NotifBloqueios.IgnoreQueryFilters()
            .Where(b => b.EmpresaId == null && b.Motivo == "Teste do catálogo global").ExecuteDeleteAsync();
    }

    [SkippableFact]
    public async Task Publicar_no_escopo_da_empresa_acha_a_rotina_global()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = await _s.SemearEmpresaAsync();
        await _s.SemearCatalogoGlobalAsync(TipoEventoNotificacao.CaixaAbertoEsquecido, CanalNotificacao.InApp);
        await using var provider = _s.ConstruirProviderDaApi(papelRls: true, empresa);
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetCurrentTenant(empresa);

        await scope.ServiceProvider.GetRequiredService<INotificadorService>().PublicarEventoAsync(
            TipoEventoNotificacao.CaixaAbertoEsquecido, empresa, usuarioDestinoId: null,
            payloadJson: """{"usuarioId":"3f2b1c0e-0000-4000-8000-000000000001","valor_abertura":10}""");

        var eventos = await _s.LerEventosDaEmpresaAsync(empresa);
        eventos.Should().ContainSingle().Which.Status.Should().Be(StatusEventoNotificacao.Processado);
        (await _s.LerMensagensDaEmpresaAsync(empresa)).Should().ContainSingle(
            "a rotina, o template e o canal globais precisam ser visíveis no escopo da empresa: sem eles o evento fecha sem outbox");
    }

    /// <summary>Contexto como a API: papel NOBYPASSRLS, interceptor e tenant fixado, sem bypass.</summary>
    private EasyStockDbContext NovoDbDaEmpresaComRls(Guid empresaId)
    {
        var options = new DbContextOptionsBuilder<EasyStockDbContext>()
            .UseNpgsql(fixture.RlsClientConnectionString)
            .AddInterceptors(new SetTenantOnConnectionInterceptor())
            .Options;
        var db = new EasyStockDbContext(options);
        db.SetMobileTenantContext(empresaId);
        return db;
    }
}

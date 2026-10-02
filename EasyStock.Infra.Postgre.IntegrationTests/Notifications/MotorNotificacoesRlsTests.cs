using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Services.Notifications.Orchestrators;
using EasyStock.Application.Services.Notifications;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Infra.Postgre.Notifications.Maintenance;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Data.Interceptors;
using System.Collections.Concurrent;
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

    // ----- dispatcher -----

    [SkippableFact]
    public async Task Dispatcher_sob_papel_NOBYPASSRLS_envia_a_pendente_de_cada_empresa()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var a = await _s.SemearAsync(CanalNotificacao.Email);
        var b = await _s.SemearAsync(CanalNotificacao.Email);
        var mensagemA = await _s.SemearMensagemAsync(a, CanalNotificacao.Email);
        var mensagemB = await _s.SemearMensagemAsync(b, CanalNotificacao.Email);
        var email = new CanalFalso(CanalNotificacao.Email, _ => new ResultadoEnvio(true, "smtp"));
        await using var provider = _s.ConstruirProviderDoWorker(papelRls: true, canais: email);

        await provider.GetRequiredService<INotificacoesDispatcherOrchestrator>().ExecutarRodadaAsync(shardCount: 4, batchSize: 50);

        (await _s.LerMensagemAsync(mensagemA.Id)).Status.Should().Be(StatusOutbox.Enviado);
        (await _s.LerMensagemAsync(mensagemB.Id)).Status.Should().Be(StatusOutbox.Enviado);
        email.ChamadasDe(mensagemA.Id).Should().Be(1);
        email.ChamadasDe(mensagemB.Id).Should().Be(1);
    }

    /// <summary>Canal que, no escopo da mensagem, conta quantas linhas do outbox o tenant enxerga (a RLS é a única camada no Worker).</summary>
    private sealed class CanalQueEspia(EasyStockDbContext db, ConcurrentDictionary<Guid, List<Guid>> visiveisPorEmpresa)
        : ICanalNotificacao
    {
        public CanalNotificacao Canal => CanalNotificacao.Email;

        public async Task<ResultadoEnvio> EnviarAsync(MensagemPronta mensagem, CancellationToken ct = default)
        {
            var ids = await db.NotifOutboxMensagens.IgnoreQueryFilters().Select(m => m.Id).ToListAsync(ct);
            visiveisPorEmpresa.AddOrUpdate(mensagem.EmpresaId, _ => ids, (_, __) => ids);
            return new ResultadoEnvio(true, "smtp");
        }
    }

    [SkippableFact]
    public async Task Dispatcher_escopo_por_item_so_enxerga_as_linhas_da_empresa_da_mensagem()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var a = await _s.SemearAsync(CanalNotificacao.Email);
        var b = await _s.SemearAsync(CanalNotificacao.Email);
        var deA = new[] { await _s.SemearMensagemAsync(a, CanalNotificacao.Email), await _s.SemearMensagemAsync(a, CanalNotificacao.Email) };
        var deB = new[] { await _s.SemearMensagemAsync(b, CanalNotificacao.Email), await _s.SemearMensagemAsync(b, CanalNotificacao.Email) };
        var visiveis = new ConcurrentDictionary<Guid, List<Guid>>();
        await using var provider = _s.ConstruirProviderDoWorker(papelRls: true,
            ajustar: services => services.AddScoped<ICanalNotificacao>(sp =>
                new CanalQueEspia(sp.GetRequiredService<EasyStockDbContext>(), visiveis)));

        await provider.GetRequiredService<INotificacoesDispatcherOrchestrator>().ExecutarRodadaAsync(shardCount: 4, batchSize: 50);

        visiveis.Keys.Should().Contain([a.EmpresaId, b.EmpresaId], "as duas empresas foram processadas");
        visiveis[a.EmpresaId].Should().BeEquivalentTo(deA.Select(m => m.Id), "o tenant da mensagem só vê o outbox da própria empresa");
        visiveis[b.EmpresaId].Should().BeEquivalentTo(deB.Select(m => m.Id));
        (await _s.LerMensagemAsync(deA[0].Id)).Status.Should().Be(StatusOutbox.Enviado);
    }

    // ----- avaliador, coletor e anonimizador -----

    [SkippableFact]
    public async Task Avaliador_sob_papel_NOBYPASSRLS_enfileira_o_evento_pendente_com_a_rotina_global()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = await _s.SemearEmpresaAsync();
        await _s.SemearCatalogoGlobalAsync(TipoEventoNotificacao.TarefaPendente, CanalNotificacao.Email, CategoriaConteudoNotificacao.Transacional);
        var evento = await _s.SemearEventoPendenteAsync(empresa, TipoEventoNotificacao.TarefaPendente);
        await using var provider = _s.ConstruirProviderDoWorker(papelRls: true);

        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<INotificacoesAvaliadorOrchestrator>().ExecutarRodadaAsync(TimeSpan.FromMinutes(2));

        (await _s.LerEventoAsync(evento.Id)).Status.Should().Be(StatusEventoNotificacao.Processado);
        var mensagens = await _s.LerMensagensDoEventoAsync(evento.Id);
        mensagens.Should().ContainSingle("a rotina global é visível ao tenant da empresa do evento: nasce o outbox dela")
            .Which.EmpresaId.Should().Be(empresa);
    }

    [SkippableFact]
    public async Task Coletor_sob_papel_NOBYPASSRLS_enxerga_os_lotes_de_todas_as_empresas()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresaA = await _s.SemearEmpresaAsync();
        var empresaB = await _s.SemearEmpresaAsync();
        var itemA = await SemearLoteVencendoEm3DiasAsync(empresaA);
        var itemB = await SemearLoteVencendoEm3DiasAsync(empresaB);
        await using var provider = _s.ConstruirProviderDoWorker(papelRls: true);

        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<INotificacoesColetorOrchestrator>().ExecutarRodadaAsync();

        (await _s.LerEventosDaEmpresaAsync(empresaA)).Should().Contain(e =>
            e.Tipo == TipoEventoNotificacao.ProdutoVencendo && e.RefEntidadeId == itemA);
        (await _s.LerEventosDaEmpresaAsync(empresaB)).Should().Contain(e =>
            e.Tipo == TipoEventoNotificacao.ProdutoVencendo && e.RefEntidadeId == itemB);
    }

    private async Task<Guid> SemearLoteVencendoEm3DiasAsync(Guid empresaId)
    {
        var lote = Lote.Criar(empresaId, $"LOT-{Guid.NewGuid():N}"[..20]);
        var item = new LoteItem
        {
            Id = Guid.NewGuid(), LoteId = lote.Id, Nome = "Bolo de pote", Quantidade = 10, CriadoEm = DateTime.UtcNow,
            ExpiraEm = DateTime.UtcNow.Date.AddDays(3).AddHours(12)
        };
        await using var db = fixture.CreateDbContext();
        using var _ = db.UseRowLevelSecurityBypass();
        db.Set<Lote>().Add(lote);
        db.Set<LoteItem>().Add(item);
        await db.SaveChangesAsync();
        return item.Id;
    }

    [SkippableFact]
    public async Task Anonimizador_sob_papel_NOBYPASSRLS_anonimiza_o_outbox_antigo()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var s = await _s.SemearAsync(CanalNotificacao.Email);
        var antiga = await _s.SemearMensagemAsync(s, CanalNotificacao.Email, ajustar: m =>
        {
            m.CriadoEm = DateTime.UtcNow.AddDays(-100);
            m.Status = StatusOutbox.Enviado;
        });
        var recente = await _s.SemearMensagemAsync(s, CanalNotificacao.Email, ajustar: m => m.Status = StatusOutbox.Enviado);
        await using var provider = _s.ConstruirProviderDoWorker(papelRls: true);
        var anonimizador = new AnonimizarLogsAntigosService(
            provider, Options.Create(new NotificationsHostingOptions()), NullLogger<AnonimizarLogsAntigosService>.Instance);

        await anonimizador.ExecutarAnonimizacaoAsync(retencaoDias: 90, CancellationToken.None);

        (await _s.LerMensagemAsync(antiga.Id)).Destinatario.Should().Be("[anonimizado]");
        (await _s.LerMensagemAsync(recente.Id)).Destinatario.Should().NotBe("[anonimizado]", "só passa da retenção o que tem mais de 90 dias");
    }

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

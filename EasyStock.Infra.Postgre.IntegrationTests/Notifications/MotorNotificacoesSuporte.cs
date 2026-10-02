using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using EasyStock.Application.DependencyInjection;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Async;
using EasyStock.Infra.Postgre.Concurrency;
using EasyStock.Infra.Postgre.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Notifications;

/// <summary>
/// Canal que guarda cada chamada recebida (N1). As contagens são por mensagem ou por empresa do teste, nunca globais:
/// o outbox é compartilhado pela classe e uma sobra de outro teste não pode mudar o resultado deste.
/// </summary>
internal sealed class CanalFalso(CanalNotificacao canal, Func<MensagemPronta, ResultadoEnvio> responder) : ICanalNotificacao
{
    private readonly ConcurrentQueue<MensagemPronta> _chamadas = new();

    public CanalNotificacao Canal => canal;

    public int ChamadasDe(Guid outboxId) => _chamadas.Count(m => m.OutboxId == outboxId);

    public int ChamadasDaEmpresa(Guid empresaId) => _chamadas.Count(m => m.EmpresaId == empresaId);

    public Task<ResultadoEnvio> EnviarAsync(MensagemPronta mensagem, CancellationToken ct = default)
    {
        _chamadas.Enqueue(mensagem);
        return Task.FromResult(responder(mensagem));
    }
}

internal sealed class RendererSimplesDoMotor : IRendererTemplate
{
    public Task<string> RenderizarAsync(string template, IDictionary<string, object?> variaveis, CancellationToken ct = default) =>
        RenderizarAsync(template, variaveis, false, ct);

    public Task<string> RenderizarAsync(string template, IDictionary<string, object?> variaveis, bool htmlEscape, CancellationToken ct = default) =>
        Task.FromResult(Regex.Replace(template, @"\{\{\s*(\w+)\s*\}\}",
            m => variaveis.TryGetValue(m.Groups[1].Value, out var v) ? v?.ToString() ?? string.Empty : string.Empty));
}

/// <summary>Sementes e leituras do motor (N1). Tudo semeado e lido pelo superusuário: a RLS é assunto do código sob teste.</summary>
internal sealed class MotorNotificacoesSuporte(PostgreSqlDatabaseFixture fixture)
{
    public const string PayloadPadrao = """{"email":"maria@example.com","telefone":"+5511999990001","token":"482913"}""";

    /// <summary>
    /// Provider como o Worker: usuário de sistema SuperAdmin (filtro global do EF desligado, só a RLS isola), canais
    /// falsos e renderer simples. <paramref name="papelRls"/> usa o login <c>rls_test_client</c> (NOBYPASSRLS).
    /// </summary>
    public ServiceProvider ConstruirProviderDoWorker(
        bool papelRls, IDictionary<string, string?>? configuracao = null, params ICanalNotificacao[] canais)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(configuracao ?? new Dictionary<string, string?>()).Build();
        var usuario = Substitute.For<ICurrentUserAccessor>();
        usuario.IsAuthenticated.Returns(true);
        usuario.Nivel.Returns(NivelAcesso.SuperAdmin);
        usuario.EmpresaId.Returns(Guid.Empty);
        return Construir(papelRls, config, usuario, canais);
    }

    /// <summary>Provider como a API: usuário comum da empresa (filtro global do EF ligado e RLS).</summary>
    public ServiceProvider ConstruirProviderDaApi(bool papelRls, Guid empresaId, params ICanalNotificacao[] canais)
    {
        var config = new ConfigurationBuilder().Build();
        var usuario = Substitute.For<ICurrentUserAccessor>();
        usuario.IsAuthenticated.Returns(true);
        usuario.Nivel.Returns(NivelAcesso.Admin);
        usuario.EmpresaId.Returns(empresaId);
        return Construir(papelRls, config, usuario, canais);
    }

    private ServiceProvider Construir(bool papelRls, IConfiguration config, ICurrentUserAccessor usuario, ICanalNotificacao[] canais)
    {
        var services = new ServiceCollection();
        services.AddSingleton(config);
        services.AddLogging();
        services.AddMemoryCache();
        services.AddHttpContextAccessor();
        services.AddSingleton(usuario);
        services.AddSingleton(Substitute.For<ICacheService>());
        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();

        services.AddEasyStockPostgreInfrastructure(papelRls ? fixture.RlsClientConnectionString : fixture.ConnectionString, config);
        services.AddEasyStockApplication();
        services.AddScoped<PostgresAdvisoryLock>();
        services.AddSingleton<IRendererTemplate, RendererSimplesDoMotor>();
        foreach (var canal in canais)
            services.AddSingleton(canal);
        return services.BuildServiceProvider();
    }

    public sealed record Semente(Guid EmpresaId, Guid EventoId, Guid TemplateId);

    public async Task<Guid> SemearEmpresaAsync()
    {
        var empresa = Empresa.Criar("Casa da Baba Motor", Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString());
        await using var db = fixture.CreateDbContext();
        using var _ = db.UseRowLevelSecurityBypass();
        db.Empresas.Add(empresa);
        await db.SaveChangesAsync();
        return empresa.Id;
    }

    /// <summary>Empresa, evento já processado e template do canal: o necessário para semear mensagens do outbox à mão.</summary>
    public async Task<Semente> SemearAsync(
        CanalNotificacao canal = CanalNotificacao.WhatsApp,
        string payloadJson = PayloadPadrao,
        TipoEventoNotificacao tipo = TipoEventoNotificacao.ResetSenha,
        DateTime? ocorridoEm = null,
        Guid? empresaId = null)
    {
        var empresa = empresaId is null
            ? Empresa.Criar("Casa da Baba Motor", Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString())
            : null;
        var dono = empresaId ?? empresa!.Id;
        var evento = EventoNotificacao.Criar(tipo, dono, payloadJson);
        evento.MarcarComoProcessado();
        if (ocorridoEm is { } quando) evento.OcorridoEm = quando;
        var template = TemplateNotificacao.Criar($"tpl-{Guid.NewGuid():N}", "Teste", canal, tipo, "Assunto", "Seu codigo: {{ token }}", dono);
        template.Aprovar("teste");
        template.Ativar();

        await using var db = fixture.CreateDbContext();
        using var _ = db.UseRowLevelSecurityBypass();
        if (empresa is not null) db.Empresas.Add(empresa);
        db.NotifEventos.Add(evento);
        db.NotifTemplates.Add(template);
        await db.SaveChangesAsync();
        return new Semente(dono, evento.Id, template.Id);
    }

    public async Task<OutboxMensagemNotificacao> SemearMensagemAsync(
        Semente s,
        CanalNotificacao canal,
        CategoriaConteudoNotificacao categoria = CategoriaConteudoNotificacao.Operacional,
        string fallbackJson = "[]",
        DateTime? agendarPara = null,
        Action<OutboxMensagemNotificacao>? ajustar = null)
    {
        var mensagem = OutboxMensagemNotificacao.Criar(s.EventoId, s.TemplateId, s.EmpresaId, canal,
            canal == CanalNotificacao.Email ? "maria@example.com" : "+5511999990001", "Assunto", "Seu codigo: 482913", categoria,
            canaisFallbackRestantesJson: fallbackJson);
        if (agendarPara is { } quando) mensagem.AgendarPara(quando);
        ajustar?.Invoke(mensagem);

        await using var db = fixture.CreateDbContext();
        using var _ = db.UseRowLevelSecurityBypass();
        db.NotifOutboxMensagens.Add(mensagem);
        await db.SaveChangesAsync();
        return mensagem;
    }

    public async Task SemearCatalogoGlobalAsync(
        TipoEventoNotificacao tipo, CanalNotificacao canal, CategoriaConteudoNotificacao categoria = CategoriaConteudoNotificacao.Operacional)
    {
        var codigo = $"global-{tipo}-{canal}".ToLowerInvariant();
        await using var db = fixture.CreateDbContext();
        using var _ = db.UseRowLevelSecurityBypass();

        if (!await db.NotifRotinas.IgnoreQueryFilters().AnyAsync(r => r.Codigo == codigo && r.EmpresaId == null))
        {
            var rotina = RotinaNotificacao.Criar(codigo, "Rotina global de teste", tipo, TriggerTipoRotina.Evento, codigo, categoria);
            rotina.CanaisOrdemFallbackJson = $"[\"{canal}\"]";
            rotina.Ativar("teste");
            db.NotifRotinas.Add(rotina);
        }

        if (!await db.NotifTemplates.IgnoreQueryFilters().AnyAsync(t => t.Codigo == codigo && t.EmpresaId == null))
        {
            var template = TemplateNotificacao.Criar(codigo, "Template global de teste", canal, tipo, "Assunto", "Seu codigo: {{ token }}");
            template.Aprovar("teste");
            template.Ativar();
            db.NotifTemplates.Add(template);
        }

        if (!await db.NotifConfiguracoesCanal.IgnoreQueryFilters().AnyAsync(c => c.Canal == canal && c.EmpresaId == null))
            db.NotifConfiguracoesCanal.Add(ConfiguracaoCanal.Criar(canal, "stub"));

        await db.SaveChangesAsync();
    }

    public async Task<OutboxMensagemNotificacao> LerMensagemAsync(Guid id)
    {
        await using var db = fixture.CreateDbContext();
        return await db.NotifOutboxMensagens.AsNoTracking().IgnoreQueryFilters().SingleAsync(m => m.Id == id);
    }

    public async Task<List<OutboxMensagemNotificacao>> LerMensagensDoEventoAsync(Guid eventoId)
    {
        await using var db = fixture.CreateDbContext();
        return await db.NotifOutboxMensagens.AsNoTracking().IgnoreQueryFilters()
            .Where(m => m.EventoId == eventoId).OrderBy(m => m.CriadoEm).ToListAsync();
    }

    public async Task<List<OutboxMensagemNotificacao>> LerMensagensDaEmpresaAsync(Guid empresaId)
    {
        await using var db = fixture.CreateDbContext();
        return await db.NotifOutboxMensagens.AsNoTracking().IgnoreQueryFilters()
            .Where(m => m.EmpresaId == empresaId).OrderBy(m => m.CriadoEm).ToListAsync();
    }

    public async Task<EventoNotificacao> LerEventoAsync(Guid id)
    {
        await using var db = fixture.CreateDbContext();
        return await db.NotifEventos.AsNoTracking().IgnoreQueryFilters().SingleAsync(e => e.Id == id);
    }

    public async Task<List<EventoNotificacao>> LerEventosDaEmpresaAsync(Guid empresaId)
    {
        await using var db = fixture.CreateDbContext();
        return await db.NotifEventos.AsNoTracking().IgnoreQueryFilters()
            .Where(e => e.EmpresaId == empresaId).OrderBy(e => e.OcorridoEm).ToListAsync();
    }

    public async Task<EventoNotificacao> SemearEventoPendenteAsync(
        Guid empresaId, TipoEventoNotificacao tipo, string payloadJson = PayloadPadrao, DateTime? ocorridoEm = null)
    {
        var evento = EventoNotificacao.Criar(tipo, empresaId, payloadJson);
        if (ocorridoEm is { } quando) evento.OcorridoEm = quando;
        await using var db = fixture.CreateDbContext();
        using var _ = db.UseRowLevelSecurityBypass();
        db.NotifEventos.Add(evento);
        await db.SaveChangesAsync();
        return evento;
    }
}

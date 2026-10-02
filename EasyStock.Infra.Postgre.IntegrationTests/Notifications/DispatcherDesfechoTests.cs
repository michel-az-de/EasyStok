using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Services.Notifications.Orchestrators;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Async;
using EasyStock.Infra.Async.DependencyInjection;
using EasyStock.Infra.Notifications.Email;
using EasyStock.Infra.Notifications.WhatsApp;
using EasyStock.Infra.Postgre.Concurrency;
using EasyStock.Infra.Postgre.DependencyInjection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Notifications;

/// <summary>
/// N2 em Postgres real: o dispatcher traduz o desfecho do canal em status do outbox. Simulado vira
/// <c>Simulado</c> (sem <c>EnviadoEm</c>, log do provider real com <c>Sucesso = false</c>); falha permanente não
/// reagenda e cai no fallback de canal; <c>Indeterminado</c> é terminal e não reenvia nem abre fallback; e a categoria
/// <c>Seguranca</c> apaga corpo, metadados e, na última mensagem aberta do evento, o payload.
/// <para>
/// Roda com o superusuário (<c>fixture.ConnectionString</c>) e o usuário de sistema do Worker (SuperAdmin), para a falha
/// vir do comportamento do dispatcher e não da RLS, que é assunto da N1. Os canais são falsos e contam as chamadas por
/// mensagem.
/// </para>
/// </summary>
public class DispatcherDesfechoTests(PostgreSqlDatabaseFixture fixture) : IClassFixture<PostgreSqlDatabaseFixture>
{
    private const string Payload = """{"token":"482913","email":"maria@example.com"}""";

    // ----- canais falsos -----

    /// <summary>
    /// Canal que guarda cada chamada recebida. As contagens são por mensagem ou por empresa do teste, nunca globais: o
    /// outbox é compartilhado pela classe e uma sobra de outro teste não pode mudar o resultado deste.
    /// </summary>
    private sealed class CanalFalso(CanalNotificacao canal, Func<MensagemPronta, ResultadoEnvio> responder) : ICanalNotificacao
    {
        private readonly ConcurrentQueue<MensagemPronta> _chamadas = new();

        public CanalNotificacao Canal => canal;

        public int ChamadasDe(Guid outboxId) => _chamadas.Count(m => m.OutboxId == outboxId);

        public int ChamadasDaEmpresa(Guid empresaId) => _chamadas.Count(m => m.EmpresaId == empresaId);

        public MensagemPronta RecebidaDe(Guid outboxId) => _chamadas.Last(m => m.OutboxId == outboxId);

        public Task<ResultadoEnvio> EnviarAsync(MensagemPronta mensagem, CancellationToken ct = default)
        {
            _chamadas.Enqueue(mensagem);
            return Task.FromResult(responder(mensagem));
        }
    }

    private sealed class RendererSimples : IRendererTemplate
    {
        public Task<string> RenderizarAsync(string template, IDictionary<string, object?> variaveis, CancellationToken ct = default) =>
            RenderizarAsync(template, variaveis, false, ct);

        public Task<string> RenderizarAsync(string template, IDictionary<string, object?> variaveis, bool htmlEscape, CancellationToken ct = default) =>
            Task.FromResult(Regex.Replace(template, @"\{\{\s*(\w+)\s*\}\}",
                m => variaveis.TryGetValue(m.Groups[1].Value, out var v) ? v?.ToString() ?? string.Empty : string.Empty));
    }

    private ServiceProvider ConstruirProvider(params ICanalNotificacao[] canais)
    {
        var config = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddMemoryCache();
        services.AddHttpContextAccessor();

        // Como o Worker: sem usuário, o contexto de sistema é SuperAdmin e o filtro global do EF fica desligado.
        var usuario = Substitute.For<ICurrentUserAccessor>();
        usuario.IsAuthenticated.Returns(true);
        usuario.Nivel.Returns(NivelAcesso.SuperAdmin);
        usuario.EmpresaId.Returns(Guid.Empty);
        services.AddSingleton(usuario);
        services.AddSingleton(Substitute.For<ICacheService>());
        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();

        services.AddEasyStockPostgreInfrastructure(fixture.ConnectionString, config);
        services.AddEasyStockNotificationsRepositories();
        services.AddScoped<PostgresAdvisoryLock>();
        services.AddSingleton<IRendererTemplate, RendererSimples>();
        foreach (var canal in canais)
            services.AddSingleton(canal);

        return services.BuildServiceProvider();
    }

    private static Task<int> RodarAsync(ServiceProvider provider) =>
        provider.GetRequiredService<INotificacoesDispatcherOrchestrator>().ExecutarRodadaAsync(shardCount: 4, batchSize: 50);

    // ----- sementes -----

    private sealed record Semente(Guid EmpresaId, Guid EventoId, Guid TemplateId);

    private async Task<Semente> SemearAsync(string payloadJson = Payload, TipoEventoNotificacao tipo = TipoEventoNotificacao.ResetSenha)
    {
        var empresa = Empresa.Criar("Casa da Baba Dispatcher", Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString());
        var evento = EventoNotificacao.Criar(tipo, empresa.Id, payloadJson);
        evento.MarcarComoProcessado();
        var template = TemplateNotificacao.Criar($"tpl-{Guid.NewGuid():N}", "Teste", CanalNotificacao.WhatsApp, tipo,
            "Assunto", "Seu codigo: {{ token }}", empresa.Id);
        template.Aprovar("teste");
        template.Ativar();

        await using var db = fixture.CreateDbContext();
        using var _ = db.UseRowLevelSecurityBypass();
        db.Empresas.Add(empresa);
        db.NotifEventos.Add(evento);
        db.NotifTemplates.Add(template);
        await db.SaveChangesAsync();
        return new Semente(empresa.Id, evento.Id, template.Id);
    }

    /// <summary>Rotina ativa da empresa e template do canal de fallback: o necessário para o fallback de canal existir.</summary>
    private async Task SemearFallbackAsync(Semente s, CanalNotificacao canalDoFallback, TipoEventoNotificacao tipo = TipoEventoNotificacao.ResetSenha,
        CategoriaConteudoNotificacao categoria = CategoriaConteudoNotificacao.Seguranca)
    {
        var codigo = $"rot-{Guid.NewGuid():N}";
        var rotina = RotinaNotificacao.Criar(codigo, "Rotina de teste", tipo, TriggerTipoRotina.Evento, codigo, categoria,
            empresaId: s.EmpresaId);
        rotina.Ativar("teste");
        var template = TemplateNotificacao.Criar(codigo, "Template do fallback", canalDoFallback, tipo,
            "Assunto", "Seu codigo: {{ token }}", s.EmpresaId);
        template.Aprovar("teste");
        template.Ativar();

        await using var db = fixture.CreateDbContext();
        using var _ = db.UseRowLevelSecurityBypass();
        db.NotifRotinas.Add(rotina);
        db.NotifTemplates.Add(template);
        await db.SaveChangesAsync();
    }

    private async Task<OutboxMensagemNotificacao> SemearMensagemAsync(
        Semente s,
        CanalNotificacao canal,
        CategoriaConteudoNotificacao categoria,
        string fallbackJson = "[]",
        string? metadadosJson = null,
        DateTime? agendarPara = null,
        string corpo = "Seu codigo: 482913")
    {
        var mensagem = OutboxMensagemNotificacao.Criar(s.EventoId, s.TemplateId, s.EmpresaId, canal,
            canal == CanalNotificacao.Email ? "maria@example.com" : "+5511999990001", "Redefinir senha", corpo, categoria,
            canaisFallbackRestantesJson: fallbackJson, metadadosJson: metadadosJson);
        if (agendarPara is { } quando) mensagem.AgendarPara(quando);

        await using var db = fixture.CreateDbContext();
        using var _ = db.UseRowLevelSecurityBypass();
        db.NotifOutboxMensagens.Add(mensagem);
        await db.SaveChangesAsync();
        return mensagem;
    }

    // ----- leituras -----

    private async Task<OutboxMensagemNotificacao> LerMensagemAsync(Guid id)
    {
        await using var db = fixture.CreateDbContext();
        return await db.NotifOutboxMensagens.AsNoTracking().IgnoreQueryFilters().SingleAsync(m => m.Id == id);
    }

    private async Task<List<OutboxMensagemNotificacao>> LerMensagensDoEventoAsync(Guid eventoId)
    {
        await using var db = fixture.CreateDbContext();
        return await db.NotifOutboxMensagens.AsNoTracking().IgnoreQueryFilters()
            .Where(m => m.EventoId == eventoId).OrderBy(m => m.CriadoEm).ToListAsync();
    }

    private async Task<EventoNotificacao> LerEventoAsync(Guid id)
    {
        await using var db = fixture.CreateDbContext();
        return await db.NotifEventos.AsNoTracking().IgnoreQueryFilters().SingleAsync(e => e.Id == id);
    }

    private async Task<List<LogEnvioNotificacao>> LerLogsAsync(Guid outboxId)
    {
        await using var db = fixture.CreateDbContext();
        return await db.NotifLogsEnvio.AsNoTracking().IgnoreQueryFilters()
            .Where(l => l.OutboxMensagemId == outboxId).OrderBy(l => l.Tentativa).ToListAsync();
    }

    // ----- Simulado -----

    [SkippableFact]
    public async Task Desfecho_simulado_grava_Simulado_e_o_log_do_provider_real()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var s = await SemearAsync();
        var mensagem = await SemearMensagemAsync(s, CanalNotificacao.WhatsApp, CategoriaConteudoNotificacao.Transacional);
        var whatsapp = new CanalFalso(CanalNotificacao.WhatsApp, _ => ResultadoEnvio.Simulado("stub", duracaoMs: 1));
        await using var provider = ConstruirProvider(whatsapp);

        await RodarAsync(provider);

        var gravada = await LerMensagemAsync(mensagem.Id);
        gravada.Status.Should().Be(StatusOutbox.Simulado);
        gravada.EnviadoEm.Should().BeNull("nada saiu: Simulado não é Enviado");
        gravada.ProviderUsado.Should().Be("stub");
        gravada.ErroUltimaTentativa.Should().BeNull();
        var logs = await LerLogsAsync(mensagem.Id);
        var log = logs.Should().ContainSingle().Subject;
        log.Provider.Should().Be("stub");
        log.Sucesso.Should().BeFalse("o log de envio não finge que enviou");
        log.ErroDetalhado.Should().Be("simulado");
        log.Tentativa.Should().Be(1);
        log.BypassConsentimento.Should().BeTrue("transacional ignora consentimento");
    }

    [SkippableFact]
    public async Task Stub_de_whatsapp_pelo_canal_real_deixa_o_outbox_Simulado()
    {
        // Ponta a ponta com o padrão de Notifications:WhatsApp:Provider (stub): canal e provider reais, só o banco é de teste.
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var s = await SemearAsync();
        var mensagem = await SemearMensagemAsync(s, CanalNotificacao.WhatsApp, CategoriaConteudoNotificacao.Transacional);
        var canal = new WhatsAppCanal(
            new StubWhatsAppProvider(NullLogger<StubWhatsAppProvider>.Instance), new ServiceCollection().BuildServiceProvider(),
            NullLogger<WhatsAppCanal>.Instance);
        await using var provider = ConstruirProvider(canal);

        await RodarAsync(provider);

        var gravada = await LerMensagemAsync(mensagem.Id);
        gravada.Status.Should().Be(StatusOutbox.Simulado);
        gravada.EnviadoEm.Should().BeNull();
        gravada.ProviderUsado.Should().Be("stub");
        var log = (await LerLogsAsync(mensagem.Id)).Should().ContainSingle().Subject;
        log.Provider.Should().Be("stub");
        log.Sucesso.Should().BeFalse();
    }

    [SkippableFact]
    public async Task Email_sobre_o_console_pelo_canal_real_deixa_o_outbox_Simulado_com_provider_console()
    {
        // Worker sem Smtp__*: o e-mail cai no ConsoleEmailService. Antes ficava Enviado, com provider "smtp".
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var s = await SemearAsync();
        var mensagem = await SemearMensagemAsync(s, CanalNotificacao.Email, CategoriaConteudoNotificacao.Operacional);
        var canal = new SmtpEmailCanal(
            new ConsoleEmailService(NullLogger<ConsoleEmailService>.Instance), NullLogger<SmtpEmailCanal>.Instance);
        await using var provider = ConstruirProvider(canal);

        await RodarAsync(provider);

        var gravada = await LerMensagemAsync(mensagem.Id);
        gravada.Status.Should().Be(StatusOutbox.Simulado);
        gravada.EnviadoEm.Should().BeNull();
        gravada.ProviderUsado.Should().Be("console").And.NotBe("smtp");
        var log = (await LerLogsAsync(mensagem.Id)).Should().ContainSingle().Subject;
        log.Provider.Should().Be("console");
        log.Sucesso.Should().BeFalse();
        log.ErroDetalhado.Should().Be("simulado");
    }

    [SkippableFact]
    public async Task Desfecho_enviado_continua_gravando_Enviado_com_EnviadoEm_e_log_de_sucesso()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var s = await SemearAsync();
        var mensagem = await SemearMensagemAsync(s, CanalNotificacao.WhatsApp, CategoriaConteudoNotificacao.Operacional);
        var whatsapp = new CanalFalso(CanalNotificacao.WhatsApp,
            _ => new ResultadoEnvio(true, "meta", DuracaoMs: 5) { IdExterno = "wamid.X" });
        await using var provider = ConstruirProvider(whatsapp);

        await RodarAsync(provider);

        var gravada = await LerMensagemAsync(mensagem.Id);
        gravada.Status.Should().Be(StatusOutbox.Enviado);
        gravada.EnviadoEm.Should().NotBeNull();
        gravada.ProviderUsado.Should().Be("meta");
        var log = (await LerLogsAsync(mensagem.Id)).Should().ContainSingle().Subject;
        log.Sucesso.Should().BeTrue();
        log.Provider.Should().Be("meta");
        log.BypassConsentimento.Should().BeFalse("operacional segue sujeita ao consentimento");
    }

    // ----- falha permanente e fallback -----

    [SkippableFact]
    public async Task Falha_permanente_nao_reagenda_e_cai_no_fallback_de_canal()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var s = await SemearAsync();
        await SemearFallbackAsync(s, CanalNotificacao.Email, categoria: CategoriaConteudoNotificacao.Operacional);
        var mensagem = await SemearMensagemAsync(s, CanalNotificacao.WhatsApp, CategoriaConteudoNotificacao.Operacional,
            fallbackJson: """["Email"]""");
        var whatsapp = new CanalFalso(CanalNotificacao.WhatsApp,
            _ => new ResultadoEnvio(false, "meta", "fora_da_janela_24h_sem_template", StatusHttp: 400, FalhaPermanente: true));
        var email = new CanalFalso(CanalNotificacao.Email, _ => new ResultadoEnvio(true, "smtp"));
        await using var provider = ConstruirProvider(whatsapp, email);

        await RodarAsync(provider);

        var original = await LerMensagemAsync(mensagem.Id);
        original.Status.Should().Be(StatusOutbox.Falhado, "permanente não reagenda, mesmo com tentativas sobrando");
        original.Tentativas.Should().Be(1);
        original.ErroUltimaTentativa.Should().Be("fora_da_janela_24h_sem_template");
        var log = (await LerLogsAsync(mensagem.Id)).Should().ContainSingle().Subject;
        log.Sucesso.Should().BeFalse();
        log.StatusHttp.Should().Be(400);

        var mensagens = await LerMensagensDoEventoAsync(s.EventoId);
        var fallback = mensagens.Should().HaveCount(2).And.Subject.Single(m => m.Id != mensagem.Id);
        fallback.Canal.Should().Be(CanalNotificacao.Email);
        fallback.CanaisFallbackRestantesJson.Should().Be("[]");

        // A rodada seguinte manda só o fallback: o WhatsApp não é chamado de novo.
        await RodarAsync(provider);
        whatsapp.ChamadasDe(mensagem.Id).Should().Be(1);
        whatsapp.ChamadasDaEmpresa(s.EmpresaId).Should().Be(1);
        (await LerMensagemAsync(fallback.Id)).Status.Should().Be(StatusOutbox.Enviado);
        email.ChamadasDe(fallback.Id).Should().Be(1);
    }

    [SkippableFact]
    public async Task Smtp_550_pelo_canal_real_termina_Falhado_sem_reagendar_e_com_uma_chamada()
    {
        // Ponta a ponta: o servico de e-mail (MailKit) classifica o 550 como permanente, o canal repassa o desfecho e o
        // dispatcher fecha a mensagem. Antes, o 550 era transitorio e voltava a Pendente (3 tentativas, backoff de 1, 5
        // e 30 min). A classificacao no protocolo de verdade esta em SmtpEmailServiceTests (servidor SMTP falso) e em
        // ClassificadorFalhaSmtpTests; aqui o servico devolve o resultado que ele daria para o 550.
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var s = await SemearAsync();
        var mensagem = await SemearMensagemAsync(s, CanalNotificacao.Email, CategoriaConteudoNotificacao.Operacional);
        var chamadas = 0;
        var servico = Substitute.For<IEmailService>();
        servico.EnviarAsync(Arg.Any<MensagemEmail>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                chamadas++;
                return new ResultadoEnvio(false, "smtp", "SMTP 550: Mailbox unavailable", FalhaPermanente: true);
            });
        await using var provider = ConstruirProvider(new SmtpEmailCanal(servico, NullLogger<SmtpEmailCanal>.Instance));

        await RodarAsync(provider);
        await RodarAsync(provider);

        var gravada = await LerMensagemAsync(mensagem.Id);
        gravada.Status.Should().Be(StatusOutbox.Falhado);
        gravada.Tentativas.Should().Be(1);
        gravada.ErroUltimaTentativa.Should().Contain("550");
        chamadas.Should().Be(1, "uma chamada só, e a segunda rodada não a repete");
        var log = (await LerLogsAsync(mensagem.Id)).Should().ContainSingle().Subject;
        log.Sucesso.Should().BeFalse();
        log.Provider.Should().Be("smtp");
    }

    [SkippableFact]
    public async Task Falha_transitoria_volta_a_Pendente_com_backoff_e_nao_abre_fallback()
    {
        // Controle: o caminho antigo segue igual para o que pode passar.
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var s = await SemearAsync();
        await SemearFallbackAsync(s, CanalNotificacao.Email, categoria: CategoriaConteudoNotificacao.Operacional);
        var mensagem = await SemearMensagemAsync(s, CanalNotificacao.WhatsApp, CategoriaConteudoNotificacao.Operacional,
            fallbackJson: """["Email"]""");
        var whatsapp = new CanalFalso(CanalNotificacao.WhatsApp, _ => new ResultadoEnvio(false, "twilio", "HTTP 429", StatusHttp: 429));
        await using var provider = ConstruirProvider(whatsapp);

        await RodarAsync(provider);

        var gravada = await LerMensagemAsync(mensagem.Id);
        gravada.Status.Should().Be(StatusOutbox.Pendente);
        gravada.Tentativas.Should().Be(1);
        gravada.ProximaTentativaEm.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(1), TimeSpan.FromSeconds(30), "backoff de 1 min");
        (await LerMensagensDoEventoAsync(s.EventoId)).Should().ContainSingle("o fallback só abre quando a mensagem termina Falhada");

        // Higiene: a mensagem ficaria elegível em 1 min e outro teste da classe a enviaria pelos canais falsos dele.
        await using var db = fixture.CreateDbContext();
        using var _ = db.UseRowLevelSecurityBypass();
        await db.NotifOutboxMensagens.IgnoreQueryFilters().Where(m => m.Id == mensagem.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(m => m.Status, StatusOutbox.Cancelado));
    }

    // ----- Indeterminado -----

    [SkippableFact]
    public async Task Indeterminado_nao_reenvia_nem_cai_no_fallback()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var s = await SemearAsync();
        await SemearFallbackAsync(s, CanalNotificacao.Email, categoria: CategoriaConteudoNotificacao.Operacional);
        var mensagem = await SemearMensagemAsync(s, CanalNotificacao.WhatsApp, CategoriaConteudoNotificacao.Operacional,
            fallbackJson: """["Email"]""");
        var whatsapp = new CanalFalso(CanalNotificacao.WhatsApp,
            _ => ResultadoEnvio.Indeterminado("twilio", "Twilio respondeu HTTP 503", statusHttp: 503));
        var email = new CanalFalso(CanalNotificacao.Email, _ => new ResultadoEnvio(true, "smtp"));
        await using var provider = ConstruirProvider(whatsapp, email);

        await RodarAsync(provider);
        await RodarAsync(provider);

        var gravada = await LerMensagemAsync(mensagem.Id);
        gravada.Status.Should().Be(StatusOutbox.Indeterminado, "terminal: a Twilio pode ter entregue");
        gravada.Tentativas.Should().Be(1);
        gravada.EnviadoEm.Should().BeNull();
        gravada.ProviderUsado.Should().Be("twilio");
        gravada.ErroUltimaTentativa.Should().Be("Twilio respondeu HTTP 503");
        whatsapp.ChamadasDaEmpresa(s.EmpresaId).Should().Be(1, "sem reenvio");
        email.ChamadasDaEmpresa(s.EmpresaId).Should().Be(0, "sem fallback de canal: mandar pelo e-mail duplicaria a mensagem");
        (await LerMensagensDoEventoAsync(s.EventoId)).Should().ContainSingle();
        var log = (await LerLogsAsync(mensagem.Id)).Should().ContainSingle().Subject;
        log.Sucesso.Should().BeFalse();
        log.Provider.Should().Be("twilio");
        log.StatusHttp.Should().Be(503);
    }

    // ----- Seguranca -----

    private static readonly Func<MensagemPronta, ResultadoEnvio> Enviou = _ => new ResultadoEnvio(true, "smtp");

    [SkippableFact]
    public async Task Seguranca_enviada_apaga_corpo_metadados_e_o_payload_so_quando_a_ultima_termina()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var s = await SemearAsync();
        const string metadados = """{"template":"reset","param1":"482913"}""";
        var porEmail = await SemearMensagemAsync(s, CanalNotificacao.Email, CategoriaConteudoNotificacao.Seguranca, metadadosJson: metadados);
        // A segunda mensagem do evento só fica elegível depois: a primeira não é a última aberta.
        var porWhatsApp = await SemearMensagemAsync(s, CanalNotificacao.WhatsApp, CategoriaConteudoNotificacao.Seguranca,
            metadadosJson: metadados, agendarPara: DateTime.UtcNow.AddHours(1));
        var email = new CanalFalso(CanalNotificacao.Email, Enviou);
        var whatsapp = new CanalFalso(CanalNotificacao.WhatsApp, _ => new ResultadoEnvio(true, "meta"));
        await using var provider = ConstruirProvider(email, whatsapp);

        await RodarAsync(provider);

        // O canal recebeu o segredo inteiro: a purga vem depois do envio, nunca antes.
        var enviada = email.RecebidaDe(porEmail.Id);
        enviada.Corpo.Should().Be("Seu codigo: 482913");
        enviada.Metadados.Should().ContainKey("param1");
        var primeira = await LerMensagemAsync(porEmail.Id);
        primeira.Status.Should().Be(StatusOutbox.Enviado);
        primeira.CorpoRenderizado.Should().Be("[apagado]");
        primeira.AssuntoRenderizado.Should().BeEmpty();
        primeira.MetadadosJson.Should().BeNull();
        (await LerEventoAsync(s.EventoId)).PayloadJson.Should().Contain("482913", "ainda há mensagem aberta do evento");
        var segunda = await LerMensagemAsync(porWhatsApp.Id);
        segunda.Status.Should().Be(StatusOutbox.Pendente);
        segunda.CorpoRenderizado.Should().Be("Seu codigo: 482913", "aberta, ainda precisa do corpo");
        whatsapp.ChamadasDaEmpresa(s.EmpresaId).Should().Be(0);

        // A segunda fica elegível e termina: agora é a última, e o payload some no mesmo commit.
        await using (var db = fixture.CreateDbContext())
        {
            using var _ = db.UseRowLevelSecurityBypass();
            await db.NotifOutboxMensagens.IgnoreQueryFilters().Where(m => m.Id == porWhatsApp.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(m => m.ProximaTentativaEm, DateTime.UtcNow.AddMinutes(-1)));
        }

        await RodarAsync(provider);

        var ultima = await LerMensagemAsync(porWhatsApp.Id);
        ultima.Status.Should().Be(StatusOutbox.Enviado);
        ultima.CorpoRenderizado.Should().Be("[apagado]");
        ultima.MetadadosJson.Should().BeNull();
        (await LerEventoAsync(s.EventoId)).PayloadJson.Should().Be("{}");
        (await LerEventoAsync(s.EventoId)).Status.Should().Be(StatusEventoNotificacao.Processado);
    }

    [SkippableFact]
    public async Task Seguranca_com_falha_permanente_so_apaga_o_payload_depois_do_fallback()
    {
        // O fallback de canal relê o payload do evento (NotificacoesDispatcherOrchestrator): apagá-lo antes dele quebraria o reenvio.
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var s = await SemearAsync();
        await SemearFallbackAsync(s, CanalNotificacao.Email);
        var original = await SemearMensagemAsync(s, CanalNotificacao.WhatsApp, CategoriaConteudoNotificacao.Seguranca,
            fallbackJson: """["Email"]""");
        var whatsapp = new CanalFalso(CanalNotificacao.WhatsApp,
            _ => new ResultadoEnvio(false, "meta", "numero_invalido", FalhaPermanente: true));
        // O e-mail indisponível na primeira rodada mantém o fallback aberto de forma determinística: dentro da mesma rodada
        // ele pode ou não ser processado, conforme o shard sorteado pela chave de idempotência.
        var emailResponde = (Func<MensagemPronta, ResultadoEnvio>)(_ => new ResultadoEnvio(false, "smtp", "421 tente depois"));
        var email = new CanalFalso(CanalNotificacao.Email, m => emailResponde(m));
        await using var provider = ConstruirProvider(whatsapp, email);

        await RodarAsync(provider);

        var primeira = await LerMensagemAsync(original.Id);
        primeira.Status.Should().Be(StatusOutbox.Falhado);
        primeira.CorpoRenderizado.Should().Be("[apagado]", "terminou: o segredo sai da mensagem");
        var fallback = (await LerMensagensDoEventoAsync(s.EventoId)).Single(m => m.Id != original.Id);
        fallback.Categoria.Should().Be(CategoriaConteudoNotificacao.Seguranca);
        fallback.Status.Should().Be(StatusOutbox.Pendente);
        fallback.CorpoRenderizado.Should().Contain("482913", "o fallback foi renderizado do payload, que ainda existia");
        (await LerEventoAsync(s.EventoId)).PayloadJson.Should().Contain("482913", "o fallback ainda está aberto");

        // O e-mail volta e a segunda rodada fecha o fallback: agora a última mensagem aberta do evento terminou.
        emailResponde = Enviou;
        await using (var db = fixture.CreateDbContext())
        {
            using var _ = db.UseRowLevelSecurityBypass();
            await db.NotifOutboxMensagens.IgnoreQueryFilters().Where(m => m.Id == fallback.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(m => m.ProximaTentativaEm, DateTime.UtcNow.AddMinutes(-1)));
        }

        await RodarAsync(provider);

        var fechada = await LerMensagemAsync(fallback.Id);
        fechada.Status.Should().Be(StatusOutbox.Enviado);
        fechada.CorpoRenderizado.Should().Be("[apagado]");
        (await LerEventoAsync(s.EventoId)).PayloadJson.Should().Be("{}", "a última mensagem aberta do evento terminou");
        email.RecebidaDe(fallback.Id).Corpo.Should().Contain("482913", "o canal recebeu o segredo antes da purga");
    }

    [SkippableFact]
    public async Task Seguranca_indeterminada_tambem_apaga_corpo_e_payload()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var s = await SemearAsync();
        var mensagem = await SemearMensagemAsync(s, CanalNotificacao.WhatsApp, CategoriaConteudoNotificacao.Seguranca);
        var whatsapp = new CanalFalso(CanalNotificacao.WhatsApp, _ => ResultadoEnvio.Indeterminado("meta", "timeout"));
        await using var provider = ConstruirProvider(whatsapp);

        await RodarAsync(provider);

        var gravada = await LerMensagemAsync(mensagem.Id);
        gravada.Status.Should().Be(StatusOutbox.Indeterminado);
        gravada.CorpoRenderizado.Should().Be("[apagado]");
        (await LerEventoAsync(s.EventoId)).PayloadJson.Should().Be("{}");
    }

    [SkippableFact]
    public async Task Seguranca_sem_adapter_de_canal_e_suprimida_e_apagada()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var s = await SemearAsync();
        var mensagem = await SemearMensagemAsync(s, CanalNotificacao.Sms, CategoriaConteudoNotificacao.Seguranca);
        await using var provider = ConstruirProvider(new CanalFalso(CanalNotificacao.Email, Enviou));

        await RodarAsync(provider);

        var gravada = await LerMensagemAsync(mensagem.Id);
        gravada.Status.Should().Be(StatusOutbox.Suprimido);
        gravada.CorpoRenderizado.Should().Be("[apagado]");
        (await LerEventoAsync(s.EventoId)).PayloadJson.Should().Be("{}");
    }

    [SkippableFact]
    public async Task Operacional_enviada_nao_apaga_nada()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var s = await SemearAsync();
        const string metadados = """{"template":"aviso","param1":"x"}""";
        var mensagem = await SemearMensagemAsync(s, CanalNotificacao.Email, CategoriaConteudoNotificacao.Operacional, metadadosJson: metadados);
        await using var provider = ConstruirProvider(new CanalFalso(CanalNotificacao.Email, Enviou));

        await RodarAsync(provider);

        var gravada = await LerMensagemAsync(mensagem.Id);
        gravada.Status.Should().Be(StatusOutbox.Enviado);
        gravada.CorpoRenderizado.Should().Be("Seu codigo: 482913");
        gravada.AssuntoRenderizado.Should().Be("Redefinir senha");
        gravada.MetadadosJson.Should().Contain("aviso");
        (await LerEventoAsync(s.EventoId)).PayloadJson.Should().Contain("482913");
    }
}

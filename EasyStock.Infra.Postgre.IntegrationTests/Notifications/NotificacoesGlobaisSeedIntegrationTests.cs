using EasyStock.Api.Data;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories.Notifications;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Postgre.IntegrationTests.Notifications;

/// <summary>
/// N13 em Postgres real: o seed do catálogo global é versionado. Base nova recebe tudo e a segunda execução não
/// escreve nada; template do sistema com versão maior no catálogo ganha linha nova ativa e a anterior fica inativa; o
/// que foi editado por pessoa não é sobrescrito; rotina do sistema é atualizada no lugar, mas a que a pessoa mexeu
/// (inclusive desligada) fica como está.
/// </summary>
public class NotificacoesGlobaisSeedIntegrationTests(PostgreSqlDatabaseFixture fixture) : IClassFixture<PostgreSqlDatabaseFixture>
{
    private const string CodigoTemplate = "incidente_sistema_email_v1";
    private const string CodigoRotina = "incidente_sistema_global";

    private sealed class LoggerColetor : ILogger
    {
        public List<string> Avisos { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning) Avisos.Add(formatter(state, exception));
        }
    }

    private static async Task SemearAsync(
        EasyStockDbContext db, LoggerColetor logger,
        IReadOnlyCollection<TemplateNotificacao>? templates = null, IReadOnlyCollection<RotinaNotificacao>? rotinas = null)
    {
        using var _ = db.UseRowLevelSecurityBypass();
        await NotificacoesGlobaisSeed.ExecutarAsync(db, logger, templates, rotinas);
    }

    private async Task NovaBaseAsync()
    {
        await fixture.ResetDatabaseAsync();
        await using var db = fixture.CreateDbContext();
        await SemearAsync(db, new LoggerColetor());
    }

    private static TemplateNotificacao CatalogoComVersao(int versao, string assunto)
    {
        var original = NotificacoesGlobaisSeed.BuildDefaultTemplates().Single(t => t.Codigo == CodigoTemplate);
        var novo = TemplateNotificacao.Criar(
            original.Codigo, original.Nome, original.Canal, original.TipoEvento, assunto, original.CorpoTemplate);
        novo.DefinirVersao(versao);
        return novo;
    }

    private static Task<List<TemplateNotificacao>> TemplatesGlobaisAsync(EasyStockDbContext db) =>
        db.NotifTemplates.IgnoreQueryFilters().AsNoTracking().Where(t => t.EmpresaId == null).OrderBy(t => t.Codigo).ThenBy(t => t.Versao).ToListAsync();

    private static Task<List<RotinaNotificacao>> RotinasGlobaisAsync(EasyStockDbContext db) =>
        db.NotifRotinas.IgnoreQueryFilters().AsNoTracking().Where(r => r.EmpresaId == null).OrderBy(r => r.Codigo).ToListAsync();

    [SkippableFact]
    public async Task SeedEmBaseNovaERodarDeNovoNaoMudaNada()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        await NovaBaseAsync();

        List<TemplateNotificacao> templates;
        List<RotinaNotificacao> rotinas;
        await using (var leitura = fixture.CreateDbContext())
        {
            using var _ = leitura.UseRowLevelSecurityBypass();
            templates = await TemplatesGlobaisAsync(leitura);
            rotinas = await RotinasGlobaisAsync(leitura);
        }

        templates.Should().Contain(t => t.Codigo == "convite_acesso_whatsapp_v1" && t.Ativo && t.Aprovado);
        rotinas.Single(r => r.Codigo == "resumo_diario_global").Ativa.Should().BeFalse();
        rotinas.Single(r => r.Codigo == "prazo_estourado_global").Ativa.Should().BeTrue();
        rotinas.Single(r => r.Codigo == "reset_senha_global").Categoria.Should().Be(CategoriaConteudoNotificacao.Seguranca);

        var logger = new LoggerColetor();
        await using (var segunda = fixture.CreateDbContext())
            await SemearAsync(segunda, logger);

        await using var depois = fixture.CreateDbContext();
        using var bypass = depois.UseRowLevelSecurityBypass();
        (await TemplatesGlobaisAsync(depois)).Should().BeEquivalentTo(templates);
        (await RotinasGlobaisAsync(depois)).Should().BeEquivalentTo(rotinas);
        logger.Avisos.Should().BeEmpty();
    }

    [SkippableFact]
    public async Task SeedSubstituiTemplateDoSistemaQuandoOCatalogoSobeDeVersao()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        await NovaBaseAsync();

        await using (var sobe = fixture.CreateDbContext())
            await SemearAsync(sobe, new LoggerColetor(), templates: [CatalogoComVersao(2, "EasyStok: assunto novo {{ componente }}")]);

        await using var leitura = fixture.CreateDbContext();
        using var _ = leitura.UseRowLevelSecurityBypass();
        var linhas = (await TemplatesGlobaisAsync(leitura)).Where(t => t.Codigo == CodigoTemplate).ToList();
        linhas.Should().HaveCount(2);
        linhas[0].Should().Match<TemplateNotificacao>(t => t.Versao == 1 && !t.Ativo);
        linhas[1].Should().Match<TemplateNotificacao>(t => t.Versao == 2 && t.Ativo && t.Aprovado && t.AtualizadoPor == "system");

        var vigente = await new TemplateNotificacaoRepository(leitura).GetAtivoAsync(CodigoTemplate, CanalNotificacao.Email, null);
        vigente!.Versao.Should().Be(2);
        vigente.AssuntoTemplate.Should().Contain("assunto novo");
    }

    [SkippableFact]
    public async Task SeedNaoSobrescreveTemplateEditadoPorPessoa()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        await NovaBaseAsync();

        await using (var edicao = fixture.CreateDbContext())
        {
            using var bypass = edicao.UseRowLevelSecurityBypass();
            var linha = await edicao.NotifTemplates.IgnoreQueryFilters().SingleAsync(t => t.Codigo == CodigoTemplate && t.EmpresaId == null);
            linha.AtualizarConteudo("Assunto da dona", linha.CorpoTemplate, "dona@casadababa.com.br");
            linha.Aprovar("dona@casadababa.com.br");
            linha.Ativar();
            await edicao.SaveChangesAsync();
        }

        var logger = new LoggerColetor();
        await using (var sobe = fixture.CreateDbContext())
            await SemearAsync(sobe, logger, templates: [CatalogoComVersao(2, "EasyStok: assunto novo {{ componente }}")]);

        await using var leitura = fixture.CreateDbContext();
        using var _ = leitura.UseRowLevelSecurityBypass();
        var linhas = (await TemplatesGlobaisAsync(leitura)).Where(t => t.Codigo == CodigoTemplate).ToList();
        linhas.Should().ContainSingle().Which.AssuntoTemplate.Should().Be("Assunto da dona");
        logger.Avisos.Should().ContainSingle().Which.Should().Contain(CodigoTemplate);
    }

    private const string CodigoConviteWhatsApp = "convite_acesso_whatsapp_v1";
    private const string MetadadosDoConviteV1 =
        """{"template":"convite_acesso_link","idioma":"pt_BR","param1":"{{ nome }}","param2":"{{ empresa }}"}""";

    /// <summary>Base como estava antes da N9: o WhatsApp do convite em v1, sem o botao URL.</summary>
    private async Task NovaBaseComConviteEmV1Async(string? editadoPor = null)
    {
        await NovaBaseAsync();
        await using var antigo = fixture.CreateDbContext();
        using var bypass = antigo.UseRowLevelSecurityBypass();
        var linha = await antigo.NotifTemplates.IgnoreQueryFilters()
            .SingleAsync(t => t.Codigo == CodigoConviteWhatsApp && t.EmpresaId == null);
        linha.DefinirVersao(1);
        linha.DefinirMetadados(MetadadosDoConviteV1);
        var rotina = await antigo.NotifRotinas.IgnoreQueryFilters()
            .SingleAsync(r => r.Codigo == "convite_acesso_global" && r.EmpresaId == null);
        rotina.ParametrosJson = """{"modoCanais":"todos"}"""; // antes da N9: sem a audiencia convidado
        if (editadoPor is not null)
        {
            linha.AtualizarConteudo("", "Texto da dona para o convite", editadoPor);
            linha.Aprovar(editadoPor);
            linha.Ativar();
        }

        await antigo.SaveChangesAsync();
    }

    [SkippableFact]
    public async Task SeedTrocaOV1PeloV2DoConviteQuandoNinguemEditou()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        await NovaBaseComConviteEmV1Async();

        await using (var sobe = fixture.CreateDbContext())
            await SemearAsync(sobe, new LoggerColetor());

        await using var leitura = fixture.CreateDbContext();
        using var _ = leitura.UseRowLevelSecurityBypass();
        var linhas = (await TemplatesGlobaisAsync(leitura)).Where(t => t.Codigo == CodigoConviteWhatsApp).ToList();
        linhas.Should().HaveCount(2);
        linhas[0].Should().Match<TemplateNotificacao>(t => t.Versao == 1 && !t.Ativo);
        linhas[1].Should().Match<TemplateNotificacao>(t => t.Versao == 2 && t.Ativo && t.Aprovado);
        linhas[1].MetadadosJson.Should().Contain("botaoUrl0").And.Contain("token_convite_whatsapp");
        linhas[1].CorpoTemplate.Should().Contain("botão abaixo");

        var rotina = (await RotinasGlobaisAsync(leitura)).Single(r => r.Codigo == "convite_acesso_global");
        System.Text.Json.JsonDocument.Parse(rotina.ParametrosJson).RootElement.GetProperty("audiencia").GetString()
            .Should().Be("convidado", "o seed atualiza a rotina do sistema no lugar");
    }

    [SkippableFact]
    public async Task SeedTrocaOV1PeloV2SemTocarEmTemplateEditadoPorPessoa()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        await NovaBaseComConviteEmV1Async(editadoPor: "dona@casadababa.com.br");

        var logger = new LoggerColetor();
        await using (var sobe = fixture.CreateDbContext())
            await SemearAsync(sobe, logger);

        await using var leitura = fixture.CreateDbContext();
        using var _ = leitura.UseRowLevelSecurityBypass();
        var linhas = (await TemplatesGlobaisAsync(leitura)).Where(t => t.Codigo == CodigoConviteWhatsApp).ToList();
        var linha = linhas.Should().ContainSingle("o texto editado pela dona nao e sobrescrito").Subject;
        linha.CorpoTemplate.Should().Be("Texto da dona para o convite");
        linha.Versao.Should().Be(1);
        logger.Avisos.Should().ContainSingle().Which.Should().Contain(CodigoConviteWhatsApp);
    }

    [SkippableFact]
    public async Task SeedAtualizaRotinaDoSistemaENaoReativaRotinaDesligadaPorPessoa()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        await NovaBaseAsync();

        // Estado antigo: o reset sem WhatsApp e transacional (rotina do sistema); o incidente foi desligado pela dona.
        await using (var antigo = fixture.CreateDbContext())
        {
            using var bypass = antigo.UseRowLevelSecurityBypass();
            var reset = await antigo.NotifRotinas.IgnoreQueryFilters().SingleAsync(r => r.Codigo == "reset_senha_global" && r.EmpresaId == null);
            reset.Categoria = CategoriaConteudoNotificacao.Transacional;
            reset.CanaisOrdemFallbackJson = "[\"Email\"]";
            reset.ParametrosJson = "{}";
            var incidente = await antigo.NotifRotinas.IgnoreQueryFilters().SingleAsync(r => r.Codigo == CodigoRotina && r.EmpresaId == null);
            incidente.Desativar("dona@casadababa.com.br");
            incidente.DefinirFallback("[\"Email\"]", "dona@casadababa.com.br");
            await antigo.SaveChangesAsync();
        }

        var logger = new LoggerColetor();
        await using (var sobe = fixture.CreateDbContext())
            await SemearAsync(sobe, logger);

        await using var leitura = fixture.CreateDbContext();
        using var _ = leitura.UseRowLevelSecurityBypass();
        var rotinas = await RotinasGlobaisAsync(leitura);

        var resetDepois = rotinas.Single(r => r.Codigo == "reset_senha_global");
        resetDepois.Categoria.Should().Be(CategoriaConteudoNotificacao.Seguranca);
        System.Text.Json.JsonSerializer.Deserialize<string[]>(resetDepois.CanaisOrdemFallbackJson).Should().Equal("Email", "WhatsApp");
        resetDepois.Ativa.Should().BeTrue();

        var incidenteDepois = rotinas.Single(r => r.Codigo == CodigoRotina);
        incidenteDepois.Ativa.Should().BeFalse();
        System.Text.Json.JsonSerializer.Deserialize<string[]>(incidenteDepois.CanaisOrdemFallbackJson).Should().Equal("Email");
        logger.Avisos.Should().ContainSingle().Which.Should().Contain(CodigoRotina);
    }
}

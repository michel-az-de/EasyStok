using System.Text.Json;
using System.Text.RegularExpressions;
using EasyStock.Api.Data;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Services.Notifications;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Notifications.Templating;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Api.UnitTests.Notifications;

/// <summary>
/// #1292: evento enfileirado sem rotina global não vira mensagem. O seed roda em todo startup
/// (StartupMigrationsAndSeed) e acrescenta só o que falta, então rotina nova chega sem migration.
/// </summary>
public class NotificacoesGlobaisSeedTests
{
    [Fact]
    public async Task ReembolsoEfetuadoTemRotinaETemplateDeWhatsApp()
    {
        var rotina = NotificacoesGlobaisSeed.BuildDefaultRotinas()
            .Should().ContainSingle(r => r.TipoEvento == TipoEventoNotificacao.ReembolsoEfetuado).Subject;
        rotina.CanaisOrdemFallbackJson.Should().Be("[\"WhatsApp\"]");
        rotina.Categoria.Should().Be(CategoriaConteudoNotificacao.Transacional);

        var template = NotificacoesGlobaisSeed.BuildDefaultTemplates()
            .Should().ContainSingle(t => t.Codigo == rotina.TemplateCodigo).Subject;
        template.Canal.Should().Be(CanalNotificacao.WhatsApp);
        template.TipoEvento.Should().Be(TipoEventoNotificacao.ReembolsoEfetuado);

        IRendererTemplate renderer = new ScribanRenderer(NullLogger<ScribanRenderer>.Instance);
        var corpo = await renderer.RenderizarAsync(template.CorpoTemplate, new Dictionary<string, object?>
        {
            ["nome"] = "Maria",
            ["numero"] = "AB12CD34",
            ["valor"] = "R$ 30,00",
        });
        corpo.Should().Contain("Maria").And.Contain("R$ 30,00").And.Contain("AB12CD34");
    }

    [Fact]
    public void TodaRotinaApontaParaTemplateExistente()
    {
        var codigos = NotificacoesGlobaisSeed.BuildDefaultTemplates().Select(t => t.Codigo).ToHashSet();

        NotificacoesGlobaisSeed.BuildDefaultRotinas()
            .Where(r => !codigos.Contains(r.TemplateCodigo))
            .Select(r => r.Codigo)
            .Should().BeEmpty();
    }

    private static readonly TipoEventoNotificacao[] CincoTipos =
    [
        TipoEventoNotificacao.ResetSenha, TipoEventoNotificacao.ConviteAcesso, TipoEventoNotificacao.IncidenteSistema,
        TipoEventoNotificacao.PrazoEstourado, TipoEventoNotificacao.ResumoDiario
    ];

    [Fact]
    public void TodoCanalDeCadaRotinaTemTemplateDoMesmoTipo()
    {
        var templates = NotificacoesGlobaisSeed.BuildDefaultTemplates().ToList();

        var faltando = new List<string>();
        foreach (var rotina in NotificacoesGlobaisSeed.BuildDefaultRotinas())
        {
            var canais = JsonSerializer.Deserialize<List<string>>(rotina.CanaisOrdemFallbackJson)!;
            foreach (var canal in canais.Select(Enum.Parse<CanalNotificacao>))
                if (!templates.Any(t => t.Canal == canal && t.TipoEvento == rotina.TipoEvento))
                    faltando.Add($"{rotina.Codigo}/{canal}");
        }

        faltando.Should().BeEmpty();
    }

    [Theory]
    [InlineData(TipoEventoNotificacao.ResetSenha, "reset_senha_global", CategoriaConteudoNotificacao.Seguranca, "usuario", true)]
    [InlineData(TipoEventoNotificacao.ConviteAcesso, "convite_acesso_global", CategoriaConteudoNotificacao.Seguranca, null, true)]
    [InlineData(TipoEventoNotificacao.IncidenteSistema, "incidente_sistema_global", CategoriaConteudoNotificacao.Operacional, "superadmins", true)]
    [InlineData(TipoEventoNotificacao.PrazoEstourado, "prazo_estourado_global", CategoriaConteudoNotificacao.Operacional, "gestores", true)]
    [InlineData(TipoEventoNotificacao.ResumoDiario, "resumo_diario_global", CategoriaConteudoNotificacao.Operacional, "admins", false)]
    public void CincoTiposTemRotinaComCanaisCategoriaEAudienciaDoCatalogo(
        TipoEventoNotificacao tipo, string codigo, CategoriaConteudoNotificacao categoria, string? audiencia, bool ativa)
    {
        var rotina = NotificacoesGlobaisSeed.BuildDefaultRotinas().Should().ContainSingle(r => r.Codigo == codigo).Subject;

        rotina.TipoEvento.Should().Be(tipo);
        rotina.Categoria.Should().Be(categoria);
        rotina.CanaisOrdemFallbackJson.Should().Be("[\"Email\",\"WhatsApp\"]");
        rotina.EmpresaId.Should().BeNull();
        rotina.Ativa.Should().Be(ativa);

        var parametros = JsonDocument.Parse(rotina.ParametrosJson).RootElement;
        parametros.GetProperty("modoCanais").GetString().Should().Be("todos");
        if (audiencia is null)
            parametros.TryGetProperty("audiencia", out _).Should().BeFalse();
        else
            parametros.GetProperty("audiencia").GetString().Should().Be(audiencia);
    }

    [Fact]
    public void ResumoDiarioEntraInativoNoCatalogo()
    {
        var rotinas = NotificacoesGlobaisSeed.BuildDefaultRotinas().ToList();

        rotinas.Single(r => r.Codigo == "resumo_diario_global").Ativa.Should().BeFalse();
        rotinas.Where(r => r.TipoEvento is TipoEventoNotificacao.ConviteAcesso or TipoEventoNotificacao.IncidenteSistema
                or TipoEventoNotificacao.PrazoEstourado)
            .Should().OnlyContain(r => r.Ativa);
    }

    [Fact]
    public void PrazoEstouradoTemJanelaDeSeteAVinteEDuas()
    {
        var rotina = NotificacoesGlobaisSeed.BuildDefaultRotinas().Single(r => r.Codigo == "prazo_estourado_global");

        rotina.JanelaInicio.Should().Be(new TimeOnly(7, 0));
        rotina.JanelaFim.Should().Be(new TimeOnly(22, 0));
    }

    [Fact]
    public void CanaisSemTemplateSaemDasRotinas()
    {
        var rotinas = NotificacoesGlobaisSeed.BuildDefaultRotinas().ToDictionary(r => r.Codigo);

        rotinas["ticket_respondido_cliente_global"].CanaisOrdemFallbackJson.Should().Be("[\"InApp\"]");
        rotinas["pagamento_confirmado_global"].CanaisOrdemFallbackJson.Should().Be("[\"InApp\"]");
        rotinas["alerta_estoque_critico_global"].CanaisOrdemFallbackJson.Should().Be("[\"Email\"]");
        rotinas["produto_vencendo_global"].CanaisOrdemFallbackJson.Should().Be("[\"Email\"]");
    }

    [Fact]
    public void TemplateDeAutenticacaoLevaOCodigoNoCorpoENoBotao()
    {
        var template = NotificacoesGlobaisSeed.BuildDefaultTemplates().Single(t => t.Codigo == "reset_senha_whatsapp_v1");

        template.CorpoTemplate.Should().Contain("{{ codigo }}");
        template.MetadadosJson.Should().Be(
            """{"template":"codigo_redefinir_senha","idioma":"pt_BR","param1":"{{ codigo }}","botaoUrl0":"{{ codigo }}"}""");
    }

    [Fact]
    public void ConviteJaNasceComoModeloConviteAcessoLink()
    {
        var template = NotificacoesGlobaisSeed.BuildDefaultTemplates().Single(t => t.Codigo == "convite_acesso_whatsapp_v1");

        JsonDocument.Parse(template.MetadadosJson!).RootElement.GetProperty("template").GetString()
            .Should().Be("convite_acesso_link");
    }

    [Fact]
    public void TemplatesNovosNaoUsamTravessao()
    {
        NotificacoesGlobaisSeed.BuildDefaultTemplates()
            .Where(t => CincoTipos.Contains(t.TipoEvento) && t.Codigo != "reset_senha_email_v1")
            .Select(t => t.AssuntoTemplate + t.CorpoTemplate)
            .Should().OnlyContain(texto => !texto.Contains('—'));
    }

    public static IEnumerable<object[]> TemplatesDosCincoTipos() =>
        NotificacoesGlobaisSeed.BuildDefaultTemplates()
            .Where(t => CincoTipos.Contains(t.TipoEvento) && t.Canal is CanalNotificacao.Email or CanalNotificacao.WhatsApp)
            .Select(t => new object[] { t.Codigo });

    [Theory]
    [MemberData(nameof(TemplatesDosCincoTipos))]
    public async Task TemplatesDosCincoTiposRenderizamComOExemplo(string codigo)
    {
        var template = NotificacoesGlobaisSeed.BuildDefaultTemplates().Single(t => t.Codigo == codigo);
        var variaveis = new Dictionary<string, object?>(ExemplosDeEvento.Obter(template.TipoEvento), StringComparer.OrdinalIgnoreCase);
        IRendererTemplate renderer = new ScribanRenderer(NullLogger<ScribanRenderer>.Instance);

        var partes = new List<string> { template.AssuntoTemplate, template.CorpoTemplate };
        if (template.MetadadosJson is not null)
            partes.AddRange(JsonSerializer.Deserialize<Dictionary<string, string>>(template.MetadadosJson)!.Values);

        // Variável ausente vira vazio no Scriban, então a checagem é por nome contra as chaves do exemplo.
        var usadas = partes
            .SelectMany(p => Regex.Matches(p, @"\{\{\s*([A-Za-z_]\w*)\s*\}\}").Select(m => m.Groups[1].Value))
            .Distinct().ToList();
        usadas.Should().NotBeEmpty();
        usadas.Where(v => !variaveis.ContainsKey(v)).Should().BeEmpty($"o exemplo de {template.TipoEvento} precisa cobrir {codigo}");

        var htmlEscape = template.Canal == CanalNotificacao.Email;
        var assunto = await renderer.RenderizarAsync(template.AssuntoTemplate, variaveis);
        var corpo = await renderer.RenderizarAsync(template.CorpoTemplate, variaveis, htmlEscape);
        corpo.Should().NotBeNullOrWhiteSpace().And.NotContain("{{");
        if (template.Canal == CanalNotificacao.Email) assunto.Should().NotBeNullOrWhiteSpace().And.NotContain("{{");
    }
}

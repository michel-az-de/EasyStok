using EasyStock.Api.Data;
using EasyStock.Application.Ports.Output.Notifications;
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
}

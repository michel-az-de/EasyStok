using EasyStock.Api.Data;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Notifications.Templating;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Api.UnitTests.Notifications;

/// <summary>
/// #1724-12: pagamento em disputa (<c>in_mediation</c>) vira aviso no sino da equipe (In-App, audiência "gestores"),
/// pelo catálogo que o seed grava a cada subida. Sem migração: o tipo é texto no banco.
/// </summary>
public class PagamentoEmMediacaoSeedTests
{
    [Fact]
    public async Task DisputaChegaAoSinoDaEquipeSoComNomeEReferencia()
    {
        var rotina = NotificacoesGlobaisSeed.BuildDefaultRotinas().Should()
            .ContainSingle(r => r.TipoEvento == TipoEventoNotificacao.PagamentoEmMediacao).Subject;
        rotina.CanaisOrdemFallbackJson.Should().Be("[\"InApp\"]");
        rotina.ParametrosJson.Should().Contain("\"audiencia\":\"gestores\"");
        rotina.Ativa.Should().BeTrue();

        var template = NotificacoesGlobaisSeed.BuildDefaultTemplates()
            .Where(t => t.Codigo == rotina.TemplateCodigo).MaxBy(t => t.Versao)!;
        template.Canal.Should().Be(CanalNotificacao.InApp);
        template.TipoEvento.Should().Be(TipoEventoNotificacao.PagamentoEmMediacao);

        IRendererTemplate renderer = new ScribanRenderer(NullLogger<ScribanRenderer>.Instance);
        var vars = new Dictionary<string, object?> { ["nome"] = "Maria", ["referencia"] = "AB12CD34" };
        (await renderer.RenderizarAsync(template.CorpoTemplate, vars)).Should().Contain("Maria").And.Contain("AB12CD34");
        (await renderer.RenderizarAsync(template.AssuntoTemplate, vars)).Should().NotBeNullOrWhiteSpace();
    }
}

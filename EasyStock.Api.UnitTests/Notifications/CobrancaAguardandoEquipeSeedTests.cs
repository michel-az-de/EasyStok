using EasyStock.Api.Data;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Notifications.Templating;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Api.UnitTests.Notifications;

/// <summary>
/// #1889 (decisão do Felipe, 21/10): link vencido na última tentativa vira aviso no sino da equipe (In-App, audiência
/// "gestores"), pelo catálogo que o seed grava a cada subida. Sem migração: o tipo é texto no banco.
/// </summary>
public class CobrancaAguardandoEquipeSeedTests
{
    [Fact]
    public async Task LinkVencidoDeNovoChegaAoSinoDaEquipeSoComNomeEReferencia()
    {
        var rotina = NotificacoesGlobaisSeed.BuildDefaultRotinas().Should()
            .ContainSingle(r => r.TipoEvento == TipoEventoNotificacao.CobrancaAguardandoEquipe).Subject;
        rotina.CanaisOrdemFallbackJson.Should().Be("[\"InApp\"]");
        rotina.Categoria.Should().Be(CategoriaConteudoNotificacao.Operacional);
        rotina.ParametrosJson.Should().Contain("\"audiencia\":\"gestores\"");
        rotina.Ativa.Should().BeTrue();

        var template = NotificacoesGlobaisSeed.BuildDefaultTemplates()
            .Where(t => t.Codigo == rotina.TemplateCodigo).MaxBy(t => t.Versao)!;
        template.Canal.Should().Be(CanalNotificacao.InApp);
        template.TipoEvento.Should().Be(TipoEventoNotificacao.CobrancaAguardandoEquipe);

        IRendererTemplate renderer = new ScribanRenderer(NullLogger<ScribanRenderer>.Instance);
        var vars = new Dictionary<string, object?> { ["nome"] = "Maria", ["referencia"] = "AB12CD34" };
        var corpo = await renderer.RenderizarAsync(template.CorpoTemplate, vars);
        corpo.Should().Contain("Maria").And.Contain("AB12CD34").And.Contain("link");
        (await renderer.RenderizarAsync(template.AssuntoTemplate, vars)).Should().NotBeNullOrWhiteSpace();
    }
}

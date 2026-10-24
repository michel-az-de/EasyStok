using EasyStock.Api.Data;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Notifications.Templating;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Api.UnitTests.Notifications;

/// <summary>
/// #2004: a automática de "pagamento confirmado" sem resposta do canal (envio incerto) avisa a equipe pelo sino
/// (In-App, audiência "gestores"), pelo catálogo que o seed grava a cada subida. Sem migração: o tipo é texto no banco.
/// </summary>
public class PagamentoSemAvisoAoClienteSeedTests
{
    [Fact]
    public async Task EnvioIncertoChegaAoSinoDaEquipeSoComNomeEReferencia()
    {
        var rotina = NotificacoesGlobaisSeed.BuildDefaultRotinas().Should()
            .ContainSingle(r => r.TipoEvento == TipoEventoNotificacao.PagamentoSemAvisoAoCliente).Subject;
        rotina.CanaisOrdemFallbackJson.Should().Be("[\"InApp\"]");
        rotina.ParametrosJson.Should().Contain("\"audiencia\":\"gestores\"");
        rotina.Ativa.Should().BeTrue();

        var template = NotificacoesGlobaisSeed.BuildDefaultTemplates()
            .Where(t => t.Codigo == rotina.TemplateCodigo).MaxBy(t => t.Versao)!;
        template.Canal.Should().Be(CanalNotificacao.InApp);
        template.TipoEvento.Should().Be(TipoEventoNotificacao.PagamentoSemAvisoAoCliente);

        IRendererTemplate renderer = new ScribanRenderer(NullLogger<ScribanRenderer>.Instance);
        var vars = new Dictionary<string, object?> { ["nome"] = "Maria", ["referencia"] = "AB12CD34" };
        (await renderer.RenderizarAsync(template.CorpoTemplate, vars)).Should().Contain("Maria").And.Contain("AB12CD34");
        (await renderer.RenderizarAsync(template.AssuntoTemplate, vars)).Should().NotBeNullOrWhiteSpace();
    }
}

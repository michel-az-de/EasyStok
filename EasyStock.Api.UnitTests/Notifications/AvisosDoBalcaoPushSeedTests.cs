using EasyStock.Api.Data;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Notifications.Templating;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Api.UnitTests.Notifications;

/// <summary>
/// #1807: os quatro avisos do Balcão saem por Web Push com o console fechado. O texto leva só o primeiro nome e o
/// evento (nada de telefone, valor ou texto do cliente), e a tag é a mesma da notificação do console.
/// </summary>
public class AvisosDoBalcaoPushSeedTests
{
    public static TheoryData<TipoEventoNotificacao, string> Avisos => new()
    {
        { TipoEventoNotificacao.ContatoNovoNoBalcao, "Maria entrou em contato" },
        { TipoEventoNotificacao.ConversaEscalada, "Maria precisa de você no Balcão" },
        { TipoEventoNotificacao.PedidoPagoNoBalcao, "Maria pagou o pedido" },
        { TipoEventoNotificacao.PedidoMudouEtapaNoBalcao, "Maria: Em preparo" },
    };

    [Theory]
    [MemberData(nameof(Avisos))]
    public async Task AvisoDoBalcaoSaiPorPushSoComNomeEEvento(TipoEventoNotificacao tipo, string corpoEsperado)
    {
        var rotina = NotificacoesGlobaisSeed.BuildDefaultRotinas().Should().ContainSingle(r => r.TipoEvento == tipo).Subject;
        rotina.CanaisOrdemFallbackJson.Should().Be("[\"Push\"]");
        rotina.Categoria.Should().Be(CategoriaConteudoNotificacao.Operacional);

        var template = NotificacoesGlobaisSeed.BuildDefaultTemplates()
            .Where(t => t.Codigo == rotina.TemplateCodigo).MaxBy(t => t.Versao)!;
        template.Canal.Should().Be(CanalNotificacao.Push);

        IRendererTemplate renderer = new ScribanRenderer(NullLogger<ScribanRenderer>.Instance);
        var vars = new Dictionary<string, object?>
        {
            ["nome"] = "Maria",
            ["etapa"] = "Em preparo",
            ["tag"] = "casa-da-baba-x-1",
            // Campos que existem no payload e não podem ir para a tela de bloqueio.
            ["cliente"] = "5511988887777",
            ["motivo"] = "cliente bloqueado: deve R$ 300",
        };
        (await renderer.RenderizarAsync(template.CorpoTemplate, vars)).Should().Be(corpoEsperado);
        var assunto = await renderer.RenderizarAsync(template.AssuntoTemplate, vars);
        assunto.Should().NotContain("5511").And.NotContain("bloqueado");
        template.MetadadosJson.Should().Contain("{{ tag }}");
    }
}

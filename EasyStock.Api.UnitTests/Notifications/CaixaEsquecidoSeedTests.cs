using EasyStock.Api.Data;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Infra.Notifications.Templating;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Api.UnitTests.Notifications;

/// <summary>
/// #2258 (F1/A7): o sino do caixa esquecido sem o código técnico e sem repetir o título no corpo; os demais prazos
/// seguem com a referência; desde a #2302 sem o "há" congelado. A versão sobe para o seed atualizar quem já tem o template gravado.
/// </summary>
public class CaixaEsquecidoSeedTests
{
    private static readonly IRendererTemplate Renderer = new ScribanRenderer(NullLogger<ScribanRenderer>.Instance);

    private static Domain.Entities.Notifications.TemplateNotificacao Template(string codigo) =>
        NotificacoesGlobaisSeed.BuildDefaultTemplates().Single(t => t.Codigo == codigo);

    [Fact]
    public async Task CaixaEsquecidoNoSinoNaoRepeteOTituloNemMostraOValorCru()
    {
        var template = Template("caixa_esquecido_aberto_inapp_v1");
        var vars = new Dictionary<string, object?> { ["data_abertura"] = "28/10/2026", ["valor_abertura"] = "R$ 1.000,00" };

        var assunto = await Renderer.RenderizarAsync(template.AssuntoTemplate, vars);
        var corpo = await Renderer.RenderizarAsync(template.CorpoTemplate, vars);

        assunto.Should().Be("Caixa aberto desde 28/10/2026");
        corpo.Should().Contain("R$ 1.000,00").And.NotContain("desde").And.NotContain("{{");
        template.Versao.Should().BeGreaterThan(1, "o seed só atualiza o template gravado quando a versão sobe");
    }

    [Fact]
    public async Task PrazoDoCaixaNoSinoUsaODetalheSemReferenciaNemTituloRepetido()
    {
        var template = Template("prazo_estourado_inapp_v1");
        var vars = new Dictionary<string, object?>
        {
            ["tipo_legivel"] = "Caixa aberto de ontem", ["atraso_texto"] = "1 dia", ["referencia"] = "6BB73F77",
            ["detalhe"] = "Aberto desde 28/10/2026, saldo de abertura R$ 1.000,00. Feche-o para não acumular vendas no dia errado.",
        };

        var corpo = await Renderer.RenderizarAsync(template.CorpoTemplate, vars);

        corpo.Should().Be((string)vars["detalhe"]!);
        corpo.Should().NotContain("6BB73F77").And.NotContain("referência").And.NotContain("Caixa aberto de ontem");
        template.Versao.Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task OutrosPrazosSeguemComAFraseEAReferenciaDeSempre()
    {
        var template = Template("prazo_estourado_inapp_v1");
        var vars = new Dictionary<string, object?>
        {
            ["tipo_legivel"] = "Pedido sem início de preparo", ["atraso_texto"] = "35 minutos", ["referencia"] = "AB12CD34",
            ["cliente"] = "Marina",
        };

        var assunto = await Renderer.RenderizarAsync(template.AssuntoTemplate, vars);
        var corpo = await Renderer.RenderizarAsync(template.CorpoTemplate, vars);

        // #2302: o sino fica gravado por dias; "há 35 minutos" mentiria depois. O atraso é ancorado no disparo e a
        // idade do aviso vem da data relativa do modal.
        assunto.Should().Be("Pedido sem início de preparo");
        corpo.Should().Be("Atraso de 35 minutos no momento do aviso, cliente Marina (referência AB12CD34).");
        template.Versao.Should().BeGreaterThanOrEqualTo(3, "o seed só reescreve o template gravado quando a versão sobe");
    }

    [Fact]
    public async Task PrazoSemClienteNoSinoNaoDizHa()
    {
        var template = Template("prazo_estourado_inapp_v1");
        var vars = new Dictionary<string, object?>
        {
            ["tipo_legivel"] = "Cliente sem resposta", ["atraso_texto"] = "5 minutos", ["referencia"] = "AB12CD34",
        };

        var assunto = await Renderer.RenderizarAsync(template.AssuntoTemplate, vars);
        var corpo = await Renderer.RenderizarAsync(template.CorpoTemplate, vars);

        assunto.Should().Be("Cliente sem resposta");
        corpo.Should().Be("Atraso de 5 minutos no momento do aviso (referência AB12CD34).");
    }
}

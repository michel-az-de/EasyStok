using EasyStock.Api.Data;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Services.Notifications;
using EasyStock.Application.Services.Notifications.Anexos;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Notifications.Templating;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Api.UnitTests.Notifications;

/// <summary>
/// #2211: os avisos da equipe saem do catálogo que o seed grava. Rotina ativa só no WhatsApp e só para quem a dona
/// inscreveu; sem inscrito ninguém recebe. O do pedido leva o canhoto em imagem.
/// </summary>
public class AvisosDaEquipeSeedTests
{
    public static TheoryData<string> Avisos() => new(AvisosDaEquipe.Todos.Select(a => a.Chave));

    [Theory]
    [MemberData(nameof(Avisos))]
    public void RotinaAtivaSoNoWhatsAppParaOsInscritos(string chave)
    {
        var aviso = AvisosDaEquipe.Todos.Single(a => a.Chave == chave);

        var rotina = NotificacoesGlobaisSeed.BuildDefaultRotinas().Should()
            .ContainSingle(r => r.TipoEvento == aviso.Tipo).Subject;
        rotina.Codigo.Should().Be(aviso.RotinaCodigo);
        rotina.TemplateCodigo.Should().Be(aviso.TemplateCodigo);
        rotina.Ativa.Should().BeTrue();
        rotina.CanaisOrdemFallbackJson.Should().Be("[\"WhatsApp\"]");
        rotina.ParametrosJson.Should().Contain("\"audiencia\":\"inscritos\"");

        var template = NotificacoesGlobaisSeed.BuildDefaultTemplates().Should()
            .ContainSingle(t => t.Codigo == aviso.TemplateCodigo).Subject;
        template.Canal.Should().Be(CanalNotificacao.WhatsApp);
        template.TipoEvento.Should().Be(aviso.Tipo);
    }

    [Fact]
    public void SoOAvisoDoPedidoLevaOCanhotoEmImagem()
    {
        var templates = NotificacoesGlobaisSeed.BuildDefaultTemplates().ToList();
        var pedido = templates.Single(t => t.Codigo == AvisosDaEquipe.PedidoComCanhoto.TemplateCodigo);
        pedido.MetadadosJson.Should().Contain($"\"{AnexadorMensagem.ChaveAnexo}\":\"{ModeloAnexoCanhoto.CodigoModelo}\"")
            .And.Contain($"\"{AnexadorMensagem.ChaveFormato}\":\"png\"");

        templates.Where(t => t.Codigo is "aviso_equipe_atencao_atendimento_whatsapp_v1" or "aviso_equipe_problema_pagamento_whatsapp_v1")
            .Should().OnlyContain(t => !t.MetadadosJson!.Contains(AnexadorMensagem.ChaveAnexo));
    }

    [Theory]
    [InlineData("aviso_equipe_atencao_atendimento_whatsapp_v1", "Marina", "pediu para falar com uma pessoa")]
    [InlineData("aviso_equipe_pedido_canhoto_whatsapp_v1", "Marina", "AB12CD34")]
    [InlineData("aviso_equipe_problema_pagamento_whatsapp_v1", "Marina", "o pagamento foi recusado")]
    public async Task CorpoUsaAsChavesQueOPublicadorEnvia(string codigo, string esperado1, string esperado2)
    {
        var template = NotificacoesGlobaisSeed.BuildDefaultTemplates().Single(t => t.Codigo == codigo);
        var vars = new Dictionary<string, object?>
        {
            ["cliente"] = "Marina", ["motivo"] = "pediu para falar com uma pessoa", ["numero"] = "AB12CD34",
            ["total"] = "R$ 89,90", ["problema"] = "o pagamento foi recusado",
        };

        IRendererTemplate renderer = new ScribanRenderer(NullLogger<ScribanRenderer>.Instance);
        var corpo = await renderer.RenderizarAsync(template.CorpoTemplate, vars);

        corpo.Should().Contain(esperado1).And.Contain(esperado2).And.NotContain("{{");
    }
}

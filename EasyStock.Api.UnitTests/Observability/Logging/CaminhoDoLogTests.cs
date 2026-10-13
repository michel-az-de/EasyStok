using EasyStock.Api.Observability.Logging;
using EasyStock.Application.Seguranca;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

namespace EasyStock.Api.UnitTests.Observability.Logging;

/// <summary>I4.5 (#1698): webhook entra em log e span pelo molde da rota; corpo de autenticação não entra.</summary>
[Trait("Category", "SecurityRegression")]
public class CaminhoDoLogTests
{
    private const string Canario = "CANARIO-9f3a7c21d4";

    private static DefaultHttpContext Contexto(string caminho, string? molde)
    {
        var contexto = new DefaultHttpContext();
        contexto.Request.Path = caminho;
        if (molde is not null)
            contexto.SetEndpoint(new RouteEndpoint(_ => Task.CompletedTask, RoutePatternFactory.Parse(molde), 0, null, molde));
        return contexto;
    }

    [Fact]
    public void Webhook_registra_o_molde_da_rota()
    {
        var contexto = Contexto($"/api/webhooks/{Canario}", "api/webhooks/{provedor}");

        CaminhoDoLog.De(contexto).Should().Be("/api/webhooks/{provedor}");
    }

    [Fact]
    public void Webhook_sem_rota_casada_nao_registra_o_caminho()
    {
        var contexto = Contexto($"/api/webhooks/whatsapp/{Canario}/extra", molde: null);

        CaminhoDoLog.De(contexto).Should().Be(CaminhoDoLog.WebhookSemRota).And.NotContain(Canario);
    }

    [Fact]
    public void Fora_de_webhook_o_caminho_continua_inteiro()
    {
        var contexto = Contexto("/api/pedidos/42", "api/pedidos/{id}");

        CaminhoDoLog.De(contexto).Should().Be("/api/pedidos/42");
        CaminhoDoLog.EhWebhook(Contexto("/api/webhooksfalso/x", null)).Should().BeFalse();
    }

    [Theory]
    [InlineData("/oauth/token")]
    [InlineData("/v1/OAuth/access_token")]
    [InlineData("/api/auth/login")]
    public void Corpo_de_endpoint_de_autenticacao_nunca_e_registrado(string caminho)
    {
        var registro = CorpoDeProvedor.ParaRegistro(caminho, "{\"qualquer_campo\":\"" + Canario + "\"}");

        registro.Should().Be(CorpoDeProvedor.Omitido);
    }

    [Theory]
    [InlineData("/v1/payments/20359978")]
    [InlineData("/authorized_payments/1")]
    [InlineData("/v1/card_tokens")]
    public void Corpo_de_endpoint_comum_continua_registrado(string caminho)
    {
        CorpoDeProvedor.ParaRegistro(caminho, "{\"message\":\"not_found\"}").Should().Be("{\"message\":\"not_found\"}");
    }

    [Fact]
    public void Corpo_comum_e_redigido_antes_do_corte()
    {
        // O token atravessa o limite: cortar antes de redigir deixaria a ponta dele em claro.
        var corpo = new string('x', 480) + " {\"access_token\":\"" + Canario + Canario + "\"}";

        var registro = CorpoDeProvedor.ParaRegistro("/v1/payments/1", corpo);

        registro.Length.Should().BeLessThanOrEqualTo(500);
        registro.Should().NotContain("CANARIO");
    }
}

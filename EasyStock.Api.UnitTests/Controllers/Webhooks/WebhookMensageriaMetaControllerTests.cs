using EasyStock.Api.Controllers.Webhooks;
using FluentAssertions;
using Microsoft.AspNetCore.RateLimiting;

namespace EasyStock.Api.UnitTests.Controllers.Webhooks;

public class WebhookMensageriaMetaControllerTests
{
    [Fact]
    public void RecebimentoUsaRateLimitDedicadoDaMeta()
    {
        // Instagram e Messenger chegam da mesma Meta, em rajada e de poucos IPs: o balde
        // "public-post" (5/min, sem fila) devolvia 429 e a Meta reenviava (issue 1285, ver 1105).
        var atributo = typeof(WebhookMensageriaMetaController)
            .GetMethod(nameof(WebhookMensageriaMetaController.Receber))!
            .GetCustomAttributes(typeof(EnableRateLimitingAttribute), inherit: true)
            .Cast<EnableRateLimitingAttribute>()
            .Single();

        atributo.PolicyName.Should().Be("webhook-meta");
    }
}

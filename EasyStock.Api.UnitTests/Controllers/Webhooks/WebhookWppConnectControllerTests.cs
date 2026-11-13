using EasyStock.Api.Controllers.Webhooks;

namespace EasyStock.Api.UnitTests.Controllers.Webhooks;

public class WebhookWppConnectControllerTests
{
    [Theory]
    [InlineData(null, null, false)]
    [InlineData("curto", "curto", false)]
    [InlineData("segredo-webhook-2071", "segredo-webhook-2071", true)]
    [InlineData("segredo-webhook-outra", "segredo-webhook-2071", false)]
    [InlineData(null, "segredo-webhook-2071", false)]
    public void Callback_exige_token_valido(string? recebido, string? configurado, bool esperado) =>
        Assert.Equal(esperado, WebhookWppConnectController.TokenValido(recebido, configurado));
}

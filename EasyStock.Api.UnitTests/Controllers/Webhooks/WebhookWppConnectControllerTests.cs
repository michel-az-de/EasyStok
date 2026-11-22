using System.Security.Cryptography;
using System.Text;
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

    private const string Segredo = "segredo-webhook-2071";
    private static readonly byte[] Corpo = Encoding.UTF8.GetBytes("{\"event\":\"onmessage\",\"body\":\"pão 🍞\"}");

    private static string Assinar(byte[] corpo, string segredo) =>
        Convert.ToHexStringLower(HMACSHA512.HashData(Encoding.UTF8.GetBytes(segredo), corpo));

    private static WebhookWppConnectController.Autenticacao Autenticar(string? assinatura, string? token = null,
        string? segredo = Segredo, bool aceitarTokenNaQuery = true) =>
        WebhookWppConnectController.Autenticar(Corpo, assinatura, token, segredo, aceitarTokenNaQuery);

    [Fact]
    public void Callback_aceita_hmac_do_corpo_sem_segredo_na_url() =>
        Assert.Equal(WebhookWppConnectController.Autenticacao.Assinado, Autenticar(Assinar(Corpo, Segredo)));

    [Fact]
    public void Callback_aceita_hmac_em_maiusculas() =>
        Assert.Equal(WebhookWppConnectController.Autenticacao.Assinado, Autenticar(Assinar(Corpo, Segredo).ToUpperInvariant()));

    [Theory]
    [InlineData("outro-segredo-qualquer", Segredo)]
    [InlineData("curto", "curto")]
    [InlineData(Segredo, null)]
    public void Callback_recusa_hmac_de_outro_segredo_ou_sem_configuracao(string assinadoCom, string? configurado) =>
        Assert.Equal(WebhookWppConnectController.Autenticacao.Recusado, Autenticar(Assinar(Corpo, assinadoCom), segredo: configurado));

    [Fact]
    public void Callback_recusa_hmac_de_outro_corpo() =>
        Assert.Equal(WebhookWppConnectController.Autenticacao.Recusado,
            WebhookWppConnectController.Autenticar([.. Corpo, 32], Assinar(Corpo, Segredo), null, Segredo, true));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nao-e-hex")]
    public void Callback_recusa_assinatura_ausente_ou_malformada(string? assinatura) =>
        Assert.Equal(WebhookWppConnectController.Autenticacao.Recusado, Autenticar(assinatura));

    [Fact]
    public void Token_na_url_ainda_passa_na_transicao() =>
        Assert.Equal(WebhookWppConnectController.Autenticacao.TokenNaQuery, Autenticar(null, token: Segredo));

    [Fact]
    public void Token_na_url_e_recusado_quando_a_transicao_acaba() =>
        Assert.Equal(WebhookWppConnectController.Autenticacao.Recusado, Autenticar(null, token: Segredo, aceitarTokenNaQuery: false));
}

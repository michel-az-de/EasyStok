using EasyStock.Application.UseCases.Atendimento.Webhook;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Webhook;

/// <summary>
/// Issue 1285: legenda de mídia e localização eram descartadas; o agente e o console viam só
/// "[imagem recebida]". Agora vão para o texto da mensagem.
/// </summary>
public class WebhookWhatsAppParserTests
{
    private static MensagemRecebidaWhatsApp Parse(string tipo, string conteudoJson) =>
        WebhookWhatsAppParser.Parse($$$"""
            {"entry":[{"changes":[{"value":{
                "metadata":{"phone_number_id":"PHONE"},
                "messages":[{"from":"5511999998888","id":"wamid.1","timestamp":"1700000000","type":"{{{tipo}}}",{{{conteudoJson}}}}]
            }}]}]}
            """).Entradas.Single().Mensagens.Single();

    [Theory]
    [InlineData("image", "image/jpeg")]
    [InlineData("document", "application/pdf")]
    [InlineData("video", "video/mp4")]
    public void MidiaComLegenda_LevaLegendaAoTextoEGuardaAMidia(string tipo, string mime)
    {
        var msg = Parse(tipo, $$"""
            "{{tipo}}":{"id":"media-1","mime_type":"{{mime}}","caption":"esse aqui, de chocolate"}
            """);

        msg.TextoCorpo.Should().Be("esse aqui, de chocolate");
        msg.MidiaId.Should().Be("media-1");
        msg.MidiaMime.Should().Be(mime);
    }

    [Fact]
    public void MidiaSemLegenda_TextoNulo()
    {
        var msg = Parse("audio", "\"audio\":{\"id\":\"media-2\",\"mime_type\":\"audio/ogg; codecs=opus\"}");

        msg.TextoCorpo.Should().BeNull();
        msg.MidiaId.Should().Be("media-2");
    }

    [Fact]
    public void Localizacao_LevaNomeEnderecoECoordenadasAoTexto()
    {
        var msg = Parse("location", """
            "location":{"latitude":-23.5613,"longitude":-46.6565,"name":"Casa da Baba","address":"Av. Paulista, 1000"}
            """);

        msg.TextoCorpo.Should().Contain("Casa da Baba")
            .And.Contain("Av. Paulista, 1000")
            .And.Contain("-23.5613,-46.6565");
        // #2155: as coordenadas também chegam estruturadas, para o perfil do cliente.
        msg.Localizacao.Should().Be(new LocalizacaoWhatsApp(-23.5613, -46.6565, "Casa da Baba", "Av. Paulista, 1000"));
    }

    [Fact]
    public void LocalizacaoComCoordenadaZerada_NaoViraLocalizacao()
    {
        var msg = Parse("location", "\"location\":{\"latitude\":0,\"longitude\":0}");

        msg.Localizacao.Should().BeNull();
    }

    [Fact]
    public void LocalizacaoSoComCoordenadas_LevaCoordenadas()
    {
        var msg = Parse("location", "\"location\":{\"latitude\":-23.5,\"longitude\":-46.6}");

        msg.TextoCorpo.Should().Contain("-23.5,-46.6");
    }

    [Fact]
    public void EcoDoAppBusiness_ViraEcoComODestinoEOConteudo()
    {
        // #1417: smb_message_echoes. "from" é o número da loja, "to" é o cliente.
        var entrada = WebhookWhatsAppParser.Parse("""
            {"entry":[{"changes":[{"field":"smb_message_echoes","value":{
                "metadata":{"phone_number_id":"PHONE"},
                "message_echoes":[
                    {"from":"551192703281","to":"5511999998888","id":"wamid.eco","timestamp":"1700000100","type":"text","text":{"body":"Já sai!"}},
                    {"from":"551192703281","to":"5511999998888","id":"wamid.eco-img","timestamp":"1700000200","type":"image","image":{"id":"media-9","mime_type":"image/jpeg"}}
                ]
            }}]}]}
            """).Entradas.Single();

        entrada.Mensagens.Should().BeEmpty("eco não é mensagem recebida do cliente");
        entrada.Ecos.Should().HaveCount(2);
        var texto = entrada.Ecos[0];
        texto.Para.Should().Be("5511999998888");
        texto.Wamid.Should().Be("wamid.eco");
        texto.Tipo.Should().Be("text");
        texto.TextoCorpo.Should().Be("Já sai!");
        texto.Timestamp.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1700000100));
        entrada.Ecos[1].Tipo.Should().Be("image");
        entrada.Ecos[1].MidiaId.Should().Be("media-9");
    }

    // #2928 S2: formato oficial da Meta para mensagem vinda de anúncio Click-to-WhatsApp.
    private const string MensagemCtwa = """
        {"entry":[{"changes":[{"value":{
            "messaging_product":"whatsapp",
            "metadata":{"display_phone_number":"551133334444","phone_number_id":"PHONE"},
            "contacts":[{"profile":{"name":"Maria"},"wa_id":"5511999998888"}],
            "messages":[{"from":"5511999998888","id":"wamid.ctwa1","timestamp":"1700000000","type":"text",
                "text":{"body":"Oi, vi o anuncio"},
                "referral":{
                    "source_url":"https://fb.me/abc123",
                    "source_id":"120210000000000001",
                    "source_type":"ad",
                    "headline":"Bolo de pote com 20% off",
                    "body":"Peca pelo WhatsApp",
                    "media_type":"image",
                    "image_url":"https://scontent.xx.fbcdn.net/ad.jpg",
                    "ctwa_clid":"ARAkLkA8rmlFeiCktEJQ-QTwRiyYHAFDLMNDBH0CD3qpjd0HR4irJ6LEkR7JwFF4XvjDFvOu"
                }}]
        }}]}]}
        """;

    [Fact]
    public void MensagemDeAnuncioClickToWhatsApp_LevaOReferral()
    {
        var msg = WebhookWhatsAppParser.Parse(MensagemCtwa).Entradas.Single().Mensagens.Single();

        msg.TextoCorpo.Should().Be("Oi, vi o anuncio");
        msg.Referral.Should().Be(new ReferralAnuncioWhatsApp(
            "https://fb.me/abc123", "120210000000000001", "ad", "Bolo de pote com 20% off", "Peca pelo WhatsApp", "image",
            "ARAkLkA8rmlFeiCktEJQ-QTwRiyYHAFDLMNDBH0CD3qpjd0HR4irJ6LEkR7JwFF4XvjDFvOu"));
    }

    [Fact]
    public void MensagemSemReferral_ReferralNulo() =>
        Parse("text", "\"text\":{\"body\":\"oi\"}").Referral.Should().BeNull();

    [Fact]
    public void ReferralVazioOuDeTipoInesperado_ReferralNulo()
    {
        Parse("text", "\"text\":{\"body\":\"oi\"},\"referral\":{}").Referral.Should().BeNull();
        Parse("text", "\"text\":{\"body\":\"oi\"},\"referral\":\"x\"").Referral.Should().BeNull();
    }
}

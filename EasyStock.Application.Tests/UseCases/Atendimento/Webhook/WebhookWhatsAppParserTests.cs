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
}

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
}

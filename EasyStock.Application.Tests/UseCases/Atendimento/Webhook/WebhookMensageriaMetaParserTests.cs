using EasyStock.Application.UseCases.Atendimento.Webhook;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Webhook;

/// <summary>
/// Webhook da Meta para Instagram e Messenger (S35): <c>object</c> diz o canal, cada
/// <c>entry[].messaging[]</c> vira uma mensagem com remetente (IGSID ou PSID) e destinatário (conta ou
/// página). Leitura e entrega não têm <c>mid</c> e são ignoradas.
/// </summary>
public class WebhookMensageriaMetaParserTests
{
    [Fact]
    public void InstagramDm()
    {
        const string json = """
        {"object":"instagram","entry":[{"id":"1784","time":1790000000000,"messaging":[
          {"sender":{"id":"IGSID-9"},"recipient":{"id":"1784"},"timestamp":1790000000000,
           "message":{"mid":"aWdfZ","text":"Oi, tem bolo de pote?"}}]}]}
        """;

        var evento = WebhookMensageriaMetaParser.Parse(json);

        var m = evento.Mensagens.Should().ContainSingle().Subject;
        m.Canal.Should().Be(CanalConversa.Instagram);
        m.RecipientId.Should().Be("1784");
        m.SenderId.Should().Be("IGSID-9");
        m.Mid.Should().Be("aWdfZ");
        m.Texto.Should().Be("Oi, tem bolo de pote?");
        m.Eco.Should().BeFalse();
        m.Timestamp.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1790000000000));
    }

    [Fact]
    public void MessengerPostback()
    {
        const string json = """
        {"object":"page","entry":[{"id":"PAGE-1","time":1,"messaging":[
          {"sender":{"id":"PSID-3"},"recipient":{"id":"PAGE-1"},"timestamp":1790000000500,
           "postback":{"title":"Ver cardápio","payload":"CARDAPIO","mid":"m_pb1"}}]}]}
        """;

        var m = WebhookMensageriaMetaParser.Parse(json).Mensagens.Should().ContainSingle().Subject;

        m.Canal.Should().Be(CanalConversa.Messenger);
        m.SenderId.Should().Be("PSID-3");
        m.Mid.Should().Be("m_pb1");
        m.PostbackPayload.Should().Be("CARDAPIO");
        m.Texto.Should().Be("Ver cardápio");
    }

    [Fact]
    public void AnexoEEco()
    {
        const string json = """
        {"object":"page","entry":[{"id":"PAGE-1","time":1,"messaging":[
          {"sender":{"id":"PSID-3"},"recipient":{"id":"PAGE-1"},"timestamp":1,
           "message":{"mid":"m_img","attachments":[{"type":"image","payload":{"url":"https://cdn/x.jpg"}}]}},
          {"sender":{"id":"PAGE-1"},"recipient":{"id":"PSID-3"},"timestamp":2,
           "message":{"mid":"m_eco","text":"resposta nossa","is_echo":true}},
          {"sender":{"id":"PSID-3"},"recipient":{"id":"PAGE-1"},"timestamp":3,"read":{"watermark":2}}]}]}
        """;

        var mensagens = WebhookMensageriaMetaParser.Parse(json).Mensagens;

        mensagens.Should().HaveCount(2, "leitura não tem mid");
        mensagens[0].TipoAnexo.Should().Be("image");
        mensagens[0].Texto.Should().BeNull();
        mensagens[1].Eco.Should().BeTrue();
    }

    [Theory]
    [InlineData("""{"object":"whatsapp_business_account","entry":[]}""")]
    [InlineData("""{"object":"page"}""")]
    [InlineData("""{"entry":[{"messaging":[{"message":{"mid":"x"}}]}]}""")]
    public void ForaDoFormato_NaoLancaENaoDevolveNada(string json) =>
        WebhookMensageriaMetaParser.Parse(json).Mensagens.Should().BeEmpty();
}

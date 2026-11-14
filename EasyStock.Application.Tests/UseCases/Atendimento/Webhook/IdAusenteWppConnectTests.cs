using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Webhook;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Webhook;

// #2590: o WPPConnect monta o evento com id = msg.id._serialized; sem esse campo no instante do evento, a chave some
// do JSON e o webhook respondia 400 definitivo, perdendo a mensagem do cliente.
public class IdAusenteWppConnectTests
{
    private static readonly CredencialWppConnect Sessao =
        new(CredencialWppConnect.NomeSessao(Guid.Parse("c8ebd039-563e-4f34-a606-c5f7a788c156")), "token");
    private readonly IWppConnectSessaoClient _cliente = Substitute.For<IWppConnectSessaoClient>();

    private static string SemId(string evento = "onmessage", string chat = "") =>
        $$$"""{"event":"{{{evento}}}","session":"{{{Sessao.Sessao}}}",{{{chat}}}"from":"5511999999999@c.us","type":"chat","body":"Oi","t":1793000000,"fromMe":false}""";

    private void Recentes(string chat, params MensagemRecenteWppConnect[] mensagens) =>
        _cliente.ListarMensagensRecentesAsync(Sessao, chat, Arg.Any<CancellationToken>()).Returns(mensagens);

    [Theory]
    [InlineData("onmessage")]
    [InlineData("unreadmessages")]
    public async Task Evento_sem_id_e_processado_com_o_id_real_da_conversa(string evento)
    {
        Recentes("5511999999999@c.us",
            new("false_5511999999999@c.us_ANTIGA", 1792999000, false, "chat", "Antes"),
            new("true_5511999999999@c.us_ROBO", 1793000000, true, "chat", "Resposta"),
            new("false_5511999999999@c.us_ABC", 1793000000, false, "chat", "Oi"));

        var completo = await IdAusenteWppConnect.CompletarAsync(SemId(evento), Sessao, _cliente);

        WppConnectWebhookParser.Parse(completo).Entradas.Single().Mensagens.Single().Wamid
            .Should().Be($"wpp:{Sessao.Sessao}:false_5511999999999@c.us_ABC");
    }

    [Fact]
    public async Task ChatId_do_evento_tem_precedencia_sobre_o_remetente()
    {
        Recentes("123@lid", new MensagemRecenteWppConnect("false_123@lid_ABC", 1793000000, false, "chat", "Oi"));

        var completo = await IdAusenteWppConnect.CompletarAsync(SemId(chat: "\"chatId\":{\"_serialized\":\"123@lid\"},"), Sessao, _cliente);

        WppConnectWebhookParser.Id(System.Text.Json.JsonDocument.Parse(completo).RootElement).Should().Be("false_123@lid_ABC");
    }

    [Fact]
    public async Task Mesmo_horario_desempata_por_tipo_e_texto()
    {
        Recentes("5511999999999@c.us",
            new("false_5511999999999@c.us_FOTO", 1793000000, false, "image", null),
            new("false_5511999999999@c.us_ABC", 1793000000, false, "chat", "Oi"));

        var completo = await IdAusenteWppConnect.CompletarAsync(SemId(), Sessao, _cliente);

        WppConnectWebhookParser.Id(System.Text.Json.JsonDocument.Parse(completo).RootElement)
            .Should().Be("false_5511999999999@c.us_ABC");
    }

    [Fact]
    public async Task Sem_correspondencia_unica_a_falha_e_retentavel_e_nao_inventa_id()
    {
        Recentes("5511999999999@c.us",
            new("false_5511999999999@c.us_A", 1793000000, false, "chat", "Oi"),
            new("false_5511999999999@c.us_B", 1793000000, false, "chat", "Oi"));
        var ambiguo = () => IdAusenteWppConnect.CompletarAsync(SemId(), Sessao, _cliente);
        (await ambiguo.Should().ThrowAsync<WhatsAppCloudException>()).Which.StatusHttp.Should().Be(503);

        Recentes("5511999999999@c.us");
        var ausente = () => IdAusenteWppConnect.CompletarAsync(SemId(), Sessao, _cliente);
        (await ausente.Should().ThrowAsync<WhatsAppCloudException>()).Which.StatusHttp.Should().Be(503);
    }

    [Theory]
    [InlineData("""{"event":"onmessage","session":"S","id":"false_5511999999999@c.us_ABC","from":"5511999999999@c.us","t":1793000000}""")]
    [InlineData("""{"event":"onack","session":"S","ack":2}""")]
    [InlineData("""{"event":"onstatechange","session":"S"}""")]
    [InlineData("""{"event":"onmessage","session":"S","from":"123@g.us","t":1793000000}""")]
    [InlineData("""{"event":"onmessage","session":"S","from":"status@broadcast","t":1793000000}""")]
    public async Task Evento_com_id_ou_que_nao_e_mensagem_nao_consulta_o_WPPConnect(string raw)
    {
        (await IdAusenteWppConnect.CompletarAsync(raw, Sessao, _cliente)).Should().Be(raw);
        await _cliente.DidNotReceiveWithAnyArgs().ListarMensagensRecentesAsync(default!, default!, default);
    }
}

using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Webhook;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Webhook;

/// <summary>
/// #2515, conversa 96ca9f9e: o cliente respondeu "2" à lista de janelas da conexão provisória e o "2" chegou ao agente
/// como texto solto. Com o menu pendente, a resposta volta como o toque no botão, como na Meta e na ponte WAHA.
/// </summary>
public class EscolhaMenuWppConnectTests
{
    private const string Sessao = "ez-c8ebd039563e4f34a606c5f7a788c156";
    private const string Telefone = "5511999999999";
    private readonly IMenusNumeradosWppConnect _menus = Substitute.For<IMenusNumeradosWppConnect>();

    private static EventoWhatsApp Evento(string tipo, string? texto) => new([new(Sessao, "{}", [new(Telefone, "Felipe")],
        [new(Telefone, $"wpp:{Sessao}:false_x_1", DateTimeOffset.UnixEpoch, tipo, texto, null, null, null, null)], [], [])]);

    [Fact]
    public async Task RespostaAoMenuViraToqueNoBotao()
    {
        _menus.EscolhaAsync(Sessao, Telefone, "2", Arg.Any<CancellationToken>())
            .Returns(("acao:escolher_janela:b:2026-11-14", "14/11 12:00-15:00"));

        var evento = await EscolhaMenuWppConnect.TraduzirAsync(Evento("text", "2"), Sessao, _menus);

        var msg = evento.Entradas.Should().ContainSingle().Subject.Mensagens.Should().ContainSingle().Subject;
        msg.Tipo.Should().Be("interactive");
        msg.BotaoRespostaId.Should().Be("acao:escolher_janela:b:2026-11-14");
        msg.TextoCorpo.Should().BeNull("o clique da Meta também chega sem texto");
        msg.Wamid.Should().Be($"wpp:{Sessao}:false_x_1");
    }

    [Fact]
    public async Task TextoQueNaoEscolheEMidiaSeguemComoVieram()
    {
        var texto = await EscolhaMenuWppConnect.TraduzirAsync(Evento("text", "quero uma lasanha"), Sessao, _menus);
        var foto = await EscolhaMenuWppConnect.TraduzirAsync(Evento("image", "2"), Sessao, _menus);

        texto.Entradas[0].Mensagens[0].Tipo.Should().Be("text");
        texto.Entradas[0].Mensagens[0].TextoCorpo.Should().Be("quero uma lasanha");
        foto.Entradas[0].Mensagens[0].Tipo.Should().Be("image");
        await _menus.DidNotReceive().EscolhaAsync(Sessao, Telefone, "2", Arg.Any<CancellationToken>());
    }
}

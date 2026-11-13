using System.Net;
using System.Text.Json.Nodes;
using EasyStock.Application.UseCases.Atendimento.Webhook;
using EasyStock.Infra.Integrations.WhatsApp.Waha;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EasyStock.Infra.Integrations.UnitTests.WhatsApp.Waha;

/// <summary>
/// O webhook do WAHA vira o corpo da Meta que o <see cref="ProcessarEventoWhatsAppUseCase"/> já processa. A prova é o
/// próprio parser da Meta lendo o que o tradutor gera.
/// </summary>
public class TradutorEventoWahaTests
{
    private const string Loja = WahaFalso.NumeroDaLoja;
    private const string Cliente = WahaFalso.TelefoneDoCliente;

    private readonly WahaFalso _waha = new();
    private readonly IDistributedCache _cache = WahaFalso.Cache();
    private readonly IOptions<WahaOptions> _opcoes = WahaFalso.Opcoes();

    private TradutorEventoWaha Tradutor() =>
        new(_waha.Resolvedor(_cache, _opcoes), WahaFalso.Menus(_cache, _opcoes), _opcoes, NullLogger<TradutorEventoWaha>.Instance);

    private static string Evento(string evento, JsonObject payload, string sessao = "default", string conta = Loja) => new JsonObject
    {
        ["id"] = "evt_01jx0000000000000000000000",
        ["timestamp"] = 1730000000000,
        ["event"] = evento,
        ["session"] = sessao,
        ["me"] = new JsonObject { ["id"] = $"{conta}@c.us", ["pushName"] = "Casa da Baba" },
        ["engine"] = "GOWS",
        ["payload"] = payload,
    }.ToJsonString();

    private static JsonObject Mensagem(
        string id = $"false_{Cliente}@c.us_3EB0C0FFEE", bool deMim = false, string origem = "app", string? corpo = "Oi, quero um bolo") => new()
    {
        ["id"] = id,
        ["timestamp"] = 1730000000,
        ["from"] = $"{Cliente}@c.us",
        ["fromMe"] = deMim,
        ["source"] = origem,
        ["body"] = corpo,
        ["hasMedia"] = false,
        ["_data"] = new JsonObject { ["Info"] = new JsonObject { ["PushName"] = "Maria" } },
    };

    private async Task<EntradaWhatsApp> Traduzir(string evento)
    {
        var corpo = await Tradutor().TraduzirAsync(evento, CancellationToken.None);
        corpo.Should().NotBeNull();
        return WebhookWhatsAppParser.Parse(corpo!).Entradas.Single();
    }

    [Fact]
    public async Task MensagemDoClienteViraMensagemDaMetaNoNumeroDaLoja()
    {
        var entrada = await Traduzir(Evento("message.any", Mensagem()));

        entrada.PhoneNumberId.Should().Be(Loja);
        entrada.Contatos.Single().Should().Be(new ContatoWhatsApp(Cliente, "Maria"));
        var mensagem = entrada.Mensagens.Single();
        mensagem.De.Should().Be(Cliente);
        mensagem.Wamid.Should().Be($"false_{Cliente}@c.us_3EB0C0FFEE");
        mensagem.Tipo.Should().Be("text");
        mensagem.TextoCorpo.Should().Be("Oi, quero um bolo");
        mensagem.Timestamp.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1730000000));
    }

    [Fact]
    public async Task OQueOEasyStokMandouNaoVoltaComoEco()
    {
        var corpo = await Tradutor().TraduzirAsync(
            Evento("message.any", Mensagem(id: $"true_{Cliente}@c.us_3EB0AAA111", deMim: true, origem: "api")), CancellationToken.None);

        corpo.Should().BeNull("já está no histórico, e o eco assumiria a conversa no lugar da dona");
    }

    [Fact]
    public async Task OQueALojaEscreveuPeloCelularViraEco()
    {
        var entrada = await Traduzir(Evento("message.any", Mensagem(id: $"true_{Cliente}@c.us_3EB0BBB222", deMim: true, origem: "app", corpo: "Já te mando o Pix")));

        entrada.Mensagens.Should().BeEmpty();
        var eco = entrada.Ecos.Single();
        eco.Para.Should().Be(Cliente);
        eco.Wamid.Should().Be($"true_{Cliente}@c.us_3EB0BBB222");
        eco.TextoCorpo.Should().Be("Já te mando o Pix");
    }

    [Theory]
    [InlineData("false_120363000000@g.us_ABC_5541988887777@c.us")] // grupo
    [InlineData("false_status@broadcast_ABC_5541988887777@c.us")] // status
    [InlineData($"false_{WahaFalso.NumeroDaLoja}@c.us_ABC")] // conversa consigo mesma
    public async Task ConversaQueNaoEhComClienteEhDescartada(string id)
    {
        (await Tradutor().TraduzirAsync(Evento("message.any", Mensagem(id: id)), CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task EventoDeOutraSessaoEhDescartado()
    {
        (await Tradutor().TraduzirAsync(Evento("message.any", Mensagem(), sessao: "outra"), CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task ContaConectadaAOutroNumeroEhDescartada()
    {
        var corpo = await Tradutor().TraduzirAsync(Evento("message.any", Mensagem(), conta: "5541911112222"), CancellationToken.None);

        corpo.Should().BeNull("as conversas de outro celular não podem cair no atendimento da loja");
    }

    [Fact]
    public async Task RespostaAoMenuNumeradoViraCliqueDeBotao()
    {
        await WahaFalso.Menus(_cache, _opcoes).GuardarAsync(Cliente,
            [("acao:avaliacao:positiva:abc", "Gostei"), ("acao:avaliacao:negativa:abc", "Não gostei")], CancellationToken.None);

        var mensagem = (await Traduzir(Evento("message.any", Mensagem(corpo: "2")))).Mensagens.Single();

        mensagem.Tipo.Should().Be("interactive");
        mensagem.BotaoRespostaId.Should().Be("acao:avaliacao:negativa:abc");
        mensagem.TextoCorpo.Should().BeNull();
    }

    [Fact]
    public async Task NumeroSemMenuPendenteSegueComoTexto()
    {
        var mensagem = (await Traduzir(Evento("message.any", Mensagem(corpo: "2")))).Mensagens.Single();

        mensagem.Tipo.Should().Be("text");
        mensagem.TextoCorpo.Should().Be("2");
    }

    [Fact]
    public async Task ImagemLevaOCaminhoDoArquivoNoWahaEALegenda()
    {
        var payload = Mensagem(corpo: "Esse aqui");
        payload["hasMedia"] = true;
        payload["media"] = new JsonObject { ["url"] = "http://ez-waha:3000/api/files/default/3EB0C0FFEE.jpeg", ["mimetype"] = "image/jpeg" };

        var mensagem = (await Traduzir(Evento("message.any", payload))).Mensagens.Single();

        mensagem.Tipo.Should().Be("image");
        mensagem.MidiaId.Should().Be("api/files/default/3EB0C0FFEE.jpeg", "o download vai ao endereço configurado, nunca ao do evento");
        mensagem.MidiaMime.Should().Be("image/jpeg");
        mensagem.TextoCorpo.Should().Be("Esse aqui");
    }

    [Theory]
    [InlineData("audio/ogg; codecs=opus", "audio")]
    [InlineData("application/pdf", "document")]
    [InlineData("image/webp", "sticker")]
    [InlineData("video/mp4", "video")]
    public async Task TipoDaMidiaVemDoMime(string mime, string tipo)
    {
        var payload = Mensagem(corpo: "");
        payload["hasMedia"] = true;
        payload["media"] = new JsonObject { ["url"] = "http://ez-waha:3000/api/files/default/X.bin", ["mimetype"] = mime };

        (await Traduzir(Evento("message.any", payload))).Mensagens.Single().Tipo.Should().Be(tipo);
    }

    [Fact]
    public async Task MidiaNaoBaixadaPeloWahaChegaSemArquivo()
    {
        var payload = Mensagem(corpo: "");
        payload["hasMedia"] = true;
        payload["media"] = new JsonObject { ["url"] = null, ["mimetype"] = "image/jpeg" };

        var mensagem = (await Traduzir(Evento("message.any", payload))).Mensagens.Single();

        mensagem.Tipo.Should().Be("image");
        mensagem.MidiaId.Should().BeNull();
    }

    [Fact]
    public async Task LocalizacaoViraTextoDaLocalizacao()
    {
        var payload = Mensagem(corpo: "");
        payload["location"] = new JsonObject { ["latitude"] = -25.43, ["longitude"] = -49.27, ["description"] = "Casa" };

        var mensagem = (await Traduzir(Evento("message.any", payload))).Mensagens.Single();

        mensagem.Tipo.Should().Be("location");
        mensagem.TextoCorpo.Should().Be("Casa; -25.43,-49.27");
    }

    [Theory]
    [InlineData(1, "sent")]
    [InlineData(2, "delivered")]
    [InlineData(3, "read")]
    [InlineData(4, "read")]
    [InlineData(-1, "failed")]
    public async Task ConfirmacaoViraStatusDaMensagemGravada(int ack, string status)
    {
        var payload = new JsonObject
        {
            ["id"] = $"true_{Cliente}@c.us_3EB0AAA111",
            ["from"] = $"{Cliente}@c.us",
            ["fromMe"] = true,
            ["ack"] = ack,
        };

        var recebido = (await Traduzir(Evento("message.ack", payload))).Statuses.Single();

        recebido.Wamid.Should().Be($"true_{Cliente}@c.us_3EB0AAA111");
        recebido.Status.Should().Be(status);
    }

    [Fact]
    public async Task ConfirmacaoPeloIdOcultoEncontraAMensagemGravadaPeloTelefone()
    {
        _waha.Responder("api/default/lids/123456789012345@lid", HttpStatusCode.OK, $$"""{"lid":"123456789012345@lid","pn":"{{Cliente}}@c.us"}""");
        var payload = new JsonObject { ["id"] = "true_123456789012345@lid_3EB0AAA111", ["fromMe"] = true, ["ack"] = 3 };

        var recebido = (await Traduzir(Evento("message.ack", payload))).Statuses.Single();

        recebido.Wamid.Should().Be($"true_{Cliente}@c.us_3EB0AAA111");
        recebido.RecipientId.Should().Be(Cliente);
    }

    [Fact]
    public async Task MensagemPeloIdOcultoChegaComOTelefoneDoCliente()
    {
        _waha.Responder("api/default/lids/123456789012345@lid", HttpStatusCode.OK, $$"""{"lid":"123456789012345@lid","pn":"{{Cliente}}@c.us"}""");

        var mensagem = (await Traduzir(Evento("message.any", Mensagem(id: "false_123456789012345@lid_3EB0C0FFEE")))).Mensagens.Single();

        mensagem.De.Should().Be(Cliente);
        mensagem.Wamid.Should().Be($"false_{Cliente}@c.us_3EB0C0FFEE");
    }

    [Fact]
    public async Task ConfirmacaoPendenteOuDoClienteNaoViraStatus()
    {
        var pendente = new JsonObject { ["id"] = $"true_{Cliente}@c.us_X", ["fromMe"] = true, ["ack"] = 0 };
        var doCliente = new JsonObject { ["id"] = $"false_{Cliente}@c.us_X", ["fromMe"] = false, ["ack"] = 3 };

        (await Tradutor().TraduzirAsync(Evento("message.ack", pendente), CancellationToken.None)).Should().BeNull();
        (await Tradutor().TraduzirAsync(Evento("message.ack", doCliente), CancellationToken.None)).Should().BeNull();
    }

    [Theory]
    [InlineData("session.status")]
    [InlineData("presence.update")]
    [InlineData("message.reaction")]
    public async Task EventoQueNaoEhMensagemNemConfirmacaoNaoVaiAoAtendimento(string evento)
    {
        var payload = new JsonObject { ["status"] = "WORKING", ["name"] = "default" };

        (await Tradutor().TraduzirAsync(Evento(evento, payload), CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task CorpoQueNaoEhJsonEhDescartado()
    {
        (await Tradutor().TraduzirAsync("isso não é json", CancellationToken.None)).Should().BeNull();
    }
}

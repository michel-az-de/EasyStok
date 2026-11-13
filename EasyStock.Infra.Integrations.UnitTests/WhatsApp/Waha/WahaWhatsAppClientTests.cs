using System.Net;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Infra.Integrations.WhatsApp.Waha;
using FluentAssertions;

namespace EasyStock.Infra.Integrations.UnitTests.WhatsApp.Waha;

/// <summary>Ponte do WAHA (#2072): o que o cliente manda para o WAHA e quando ele recusa sem chamar a rede.</summary>
public class WahaWhatsAppClientTests
{
    private const string Cliente = WahaFalso.TelefoneDoCliente;

    [Fact]
    public async Task TextoSaiPelaConversaDoTelefoneComOIdDoWaha()
    {
        var waha = new WahaFalso();
        var cliente = waha.Cliente();

        var envio = await cliente.EnviarTextoAsync(Cliente, "Oi! Seu pedido saiu.", responderAWamid: "false_5541988887777@c.us_ABC");

        envio.Wamid.Should().Be($"true_{Cliente}@c.us_3EB0AAA111");
        var chamada = waha.Em("api/sendText").Single();
        using var corpo = JsonDocument.Parse(chamada.Corpo);
        corpo.RootElement.GetProperty("session").GetString().Should().Be("default");
        corpo.RootElement.GetProperty("chatId").GetString().Should().Be($"{Cliente}@c.us");
        corpo.RootElement.GetProperty("text").GetString().Should().Be("Oi! Seu pedido saiu.");
        corpo.RootElement.GetProperty("reply_to").GetString().Should().Be("false_5541988887777@c.us_ABC");
    }

    [Fact]
    public async Task SemRespostaAOutraMensagemNaoMandaReplyTo()
    {
        var waha = new WahaFalso();

        await waha.Cliente().EnviarTextoAsync(Cliente, "Oi");

        waha.Em("api/sendText").Single().Corpo.Should().NotContain("reply_to");
    }

    [Fact]
    public async Task ComPausaMandaDigitandoAntesDoTexto()
    {
        var waha = new WahaFalso();
        var cliente = waha.Cliente(opcoes: WahaFalso.Opcoes(o => { o.PausaMinimaMs = 1; o.PausaMaximaMs = 5; }));

        await cliente.EnviarTextoAsync(Cliente, "Oi");

        waha.Chamadas.Select(c => c.Caminho).Should().ContainInOrder("api/startTyping", "api/sendText");
    }

    [Fact]
    public async Task DigitandoQueFalhaNaoImpedeOEnvio()
    {
        var waha = new WahaFalso();
        waha.Responder("api/startTyping", HttpStatusCode.InternalServerError, "{}");
        var cliente = waha.Cliente(opcoes: WahaFalso.Opcoes(o => { o.PausaMinimaMs = 1; o.PausaMaximaMs = 5; }));

        var envio = await cliente.EnviarTextoAsync(Cliente, "Oi");

        envio.Wamid.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task DigitandoQueNaoRespondeNaoSeguraOEnvio()
    {
        var waha = new WahaFalso();
        waha.Penduradas.Add("api/startTyping");
        var cliente = waha.Cliente(opcoes: WahaFalso.Opcoes(o => { o.PausaMinimaMs = 1; o.PausaMaximaMs = 5; }));
        var relogio = System.Diagnostics.Stopwatch.StartNew();

        var envio = await cliente.EnviarTextoAsync(Cliente, "Oi");

        envio.Wamid.Should().NotBeNullOrEmpty();
        relogio.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(8), "o digitando desiste em 3 s");
    }

    [Fact]
    public async Task BotoesSaemNumeradosEAEscolhaFicaGuardada()
    {
        var waha = new WahaFalso();
        var cache = WahaFalso.Cache();
        var opcoes = WahaFalso.Opcoes();
        var cliente = waha.Cliente(cache, opcoes);

        await cliente.EnviarBotoesAsync(Cliente, "Qual janela?", [("acao:escolher_janela:1", "02/06 13:00-14:00"), ("acao:escolher_janela:2", "02/06 14:00-15:00")]);

        using var corpo = JsonDocument.Parse(waha.Em("api/sendText").Single().Corpo);
        corpo.RootElement.GetProperty("text").GetString().Should().Be(
            "Qual janela?\n\n1 - 02/06 13:00-14:00\n2 - 02/06 14:00-15:00\n\n" + MenuNumeradoWaha.Instrucao);
        var escolha = await WahaFalso.Menus(cache, opcoes).EscolhaAsync(Cliente, "2", CancellationToken.None);
        escolha!.Id.Should().Be("acao:escolher_janela:2");
    }

    [Fact]
    public async Task BotoesForaDaRegraDaMetaSaoRecusadosAntesDaRede()
    {
        var waha = new WahaFalso();

        var acao = () => waha.Cliente().EnviarBotoesAsync(Cliente, "Corpo", [("a", "Título com mais de vinte")]);

        await acao.Should().ThrowAsync<ArgumentException>();
        waha.Chamadas.Should().BeEmpty();
    }

    [Fact]
    public async Task ModeloAprovadoNaoExisteNaPonte()
    {
        var waha = new WahaFalso();

        var acao = () => waha.Cliente().EnviarTemplateAsync(Cliente, "pedido_saiu", "pt_BR", ["123"]);

        (await acao.Should().ThrowAsync<WhatsAppCloudException>()).Which.EhPermanente.Should().BeTrue();
        waha.Chamadas.Should().BeEmpty();
    }

    [Theory]
    [InlineData("5541911112222", true)] // empresa vinculada a outro número
    [InlineData(null, true)] // empresa sem número
    [InlineData(WahaFalso.NumeroDaLoja, false)] // sem empresa corrente
    public async Task SoEnviaParaAEmpresaVinculadaAoNumeroDaPonte(string? numeroDaEmpresa, bool haTenant)
    {
        var waha = new WahaFalso();

        var acao = () => waha.Cliente(numeroDaEmpresa: numeroDaEmpresa, haTenant: haTenant).EnviarTextoAsync(Cliente, "Oi");

        (await acao.Should().ThrowAsync<WhatsAppCloudException>()).Which.EhPermanente.Should().BeTrue();
        waha.Chamadas.Should().BeEmpty();
    }

    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity, false)] // sessão fora do ar: tenta de novo depois
    [InlineData(HttpStatusCode.NotFound, false)]
    [InlineData(HttpStatusCode.TooManyRequests, false)]
    [InlineData(HttpStatusCode.BadRequest, true)]
    [InlineData(HttpStatusCode.Unauthorized, true)]
    public async Task ErroDoWahaLevaOStatusEAClasse(HttpStatusCode status, bool permanente)
    {
        var waha = new WahaFalso();
        waha.Responder("api/sendText", status, """{"message":"Session status is not as expected"}""");

        var acao = () => waha.Cliente().EnviarTextoAsync(Cliente, "Oi");

        var erro = (await acao.Should().ThrowAsync<WhatsAppCloudException>()).Which;
        erro.StatusHttp.Should().Be((int)status);
        erro.EhPermanente.Should().Be(permanente);
    }

    [Fact]
    public async Task RespostaSemIdEhRecusaComStatus()
    {
        var waha = new WahaFalso();
        waha.Responder("api/sendText", HttpStatusCode.Created, "{}");

        var acao = () => waha.Cliente().EnviarTextoAsync(Cliente, "Oi");

        (await acao.Should().ThrowAsync<WhatsAppCloudException>()).Which.StatusHttp.Should().Be(201);
    }

    [Fact]
    public async Task ImagemVaiPelaUrlComOTipoDaExtensao()
    {
        var waha = new WahaFalso();

        await waha.Cliente().EnviarImagemAsync(Cliente, "https://api.exemplo.com/files/cardapio.png", "Cardápio");

        using var corpo = JsonDocument.Parse(waha.Em("api/sendImage").Single().Corpo);
        var arquivo = corpo.RootElement.GetProperty("file");
        arquivo.GetProperty("url").GetString().Should().Be("https://api.exemplo.com/files/cardapio.png");
        arquivo.GetProperty("mimetype").GetString().Should().Be("image/png");
        corpo.RootElement.GetProperty("caption").GetString().Should().Be("Cardápio");
    }

    [Fact]
    public async Task ImagemSemUrlAbsolutaEhRecusadaAntesDaRede()
    {
        var waha = new WahaFalso();

        var acao = () => waha.Cliente().EnviarImagemAsync(Cliente, "/files/cardapio.png");

        await acao.Should().ThrowAsync<ArgumentException>();
        waha.Chamadas.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true, "audio/ogg; codecs=opus", false)]
    [InlineData(false, "audio/mpeg", true)]
    public async Task AudioSaiComoVozEConverteQuandoPrecisa(bool notaDeVoz, string mime, bool converte)
    {
        var waha = new WahaFalso();

        await waha.Cliente().EnviarAudioAsync(Cliente, "https://api.exemplo.com/files/a.mp3", notaDeVoz);

        using var corpo = JsonDocument.Parse(waha.Em("api/sendVoice").Single().Corpo);
        corpo.RootElement.GetProperty("file").GetProperty("mimetype").GetString().Should().Be(mime);
        corpo.RootElement.GetProperty("convert").GetBoolean().Should().Be(converte);
    }

    [Fact]
    public async Task MarcarComoLidaUsaAConversaDoId()
    {
        var waha = new WahaFalso();

        await waha.Cliente().MarcarComoLidaAsync($"false_{Cliente}@c.us_ABC123");

        using var corpo = JsonDocument.Parse(waha.Em("api/sendSeen").Single().Corpo);
        corpo.RootElement.GetProperty("chatId").GetString().Should().Be($"{Cliente}@c.us");
        corpo.RootElement.GetProperty("messageIds")[0].GetString().Should().Be($"false_{Cliente}@c.us_ABC123");
    }

    [Fact]
    public async Task MarcarComoLidaComIdDeOutroFormatoNaoChamaOWaha()
    {
        var waha = new WahaFalso();

        await waha.Cliente().MarcarComoLidaAsync("wamid.HBgM");

        waha.Chamadas.Should().BeEmpty();
    }

    [Fact]
    public async Task MidiaBaixaDoCaminhoDoWahaComOTipo()
    {
        var waha = new WahaFalso();
        waha.Responder("api/files/default/ABC.jpg", HttpStatusCode.OK, "bytes", "image/jpeg");

        var (conteudo, mime) = await waha.Cliente().BaixarMidiaAsync("api/files/default/ABC.jpg");

        mime.Should().Be("image/jpeg");
        new StreamReader(conteudo).ReadToEnd().Should().Be("bytes");
    }

    [Theory]
    [InlineData("https://outro.host/arquivo.jpg")]
    [InlineData("api/files/../sessions/segredo")]
    [InlineData("api/sessions")]
    public async Task MidiaForaDaPastaDeArquivosEhRecusada(string caminho)
    {
        var waha = new WahaFalso();

        var acao = () => waha.Cliente().BaixarMidiaAsync(caminho);

        await acao.Should().ThrowAsync<WhatsAppCloudException>();
        waha.Chamadas.Should().BeEmpty();
    }

    [Fact]
    public async Task ContatoDeIdOcultoSemTelefoneRecebePelaMesmaConversa()
    {
        var waha = new WahaFalso();
        var cache = WahaFalso.Cache();
        var opcoes = WahaFalso.Opcoes();
        waha.Responder("api/default/lids/123456789012345@lid", HttpStatusCode.OK, """{"lid":"123456789012345@lid","pn":null}""");
        var contato = await waha.Resolvedor(cache, opcoes).ContatoAsync("123456789012345@lid", CancellationToken.None);

        await waha.Cliente(cache, opcoes).EnviarTextoAsync(contato!.Contato, "Oi");

        contato.Contato.Should().Be("123456789012345");
        using var corpo = JsonDocument.Parse(waha.Em("api/sendText").Single().Corpo);
        corpo.RootElement.GetProperty("chatId").GetString().Should().Be("123456789012345@lid");
    }
}

using System.Net;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Infra.Integrations.WhatsApp;
using EasyStock.TestHelpers.Http;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace EasyStock.Infra.Integrations.UnitTests.WhatsApp;

public class WppConnectClientTests
{
    private readonly HttpFalso _http = new();
    private readonly IRemetenteWhatsApp _remetente = Substitute.For<IRemetenteWhatsApp>();
    private readonly CredencialWppConnect _sessao = new("ez-c8ebd039563e4f34a606c5f7a788c156", "token-da-sessao");
    private WppConnectClient Client => new(_http.CriarClient("http://wpp.test/"),
        Options.Create(new WppConnectOptions { Habilitado = true, SecretKey = "segredo-teste", WebhookToken = "token-webhook-teste" }), _remetente);

    public WppConnectClientTests() => _remetente.ObterSessaoWebAsync(Arg.Any<CancellationToken>()).Returns(_sessao);

    [Fact]
    public async Task Envio_texto_exige_id_real_e_token_da_sessao()
    {
        _http.Quando(HttpMethod.Post, $"/api/{_sessao.Sessao}/send-message")
            .Responde(HttpStatusCode.Created, """{"status":"success","response":[{"id":{"_serialized":"true_contato_ID"}}]}""");
        var result = await Client.EnviarTextoAsync("5511999999999", "Olá");
        result.Wamid.Should().Be($"wpp:{_sessao.Sessao}:true_contato_ID");
        _http.UltimaChamada!.Cabecalho("Authorization").Should().Be("Bearer token-da-sessao");
    }

    [Theory]
    [InlineData("""{"status":"success","response":[]}""")]
    [InlineData("""{"status":"error","response":[{"id":"id-falso"}]}""")]
    public async Task Resposta_sem_confirmacao_nao_vira_sucesso(string body)
    {
        _http.QuandoQualquer().Responde(HttpStatusCode.Created, body);
        var act = () => Client.EnviarTextoAsync("5511999999999", "Olá");
        await act.Should().ThrowAsync<WhatsAppCloudException>();
        _http.Chamadas.Should().HaveCount(1);
    }

    /// <summary>
    /// #2515: a conta comum não tem botão. As opções saem numeradas e o menu fica guardado para o "2" do cliente voltar
    /// como o toque no segundo botão.
    /// </summary>
    [Fact]
    public async Task Botoes_saem_numerados_e_o_menu_fica_guardado_para_a_resposta()
    {
        var menus = Substitute.For<IMenusNumeradosWppConnect>();
        _http.Quando(HttpMethod.Post, $"/api/{_sessao.Sessao}/send-message")
            .Responde(HttpStatusCode.Created, """{"status":"success","response":[{"id":{"_serialized":"true_contato_MENU"}}]}""");
        var client = new WppConnectClient(_http.CriarClient("http://wpp.test/"),
            Options.Create(new WppConnectOptions { Habilitado = true, SecretKey = "segredo-teste", WebhookToken = "token-webhook-teste" }),
            _remetente, menus: menus);
        (string Id, string Titulo)[] opcoes = [("acao:escolher_janela:a:2026-11-14", "14/11 09:00-12:00"), ("acao:escolher_janela:b:2026-11-14", "14/11 12:00-15:00")];

        await client.EnviarBotoesAsync("5511999999999", "Escolha a janela de entrega:", opcoes);

        System.Text.Json.JsonDocument.Parse(_http.UltimaChamada!.Corpo!).RootElement.GetProperty("message").GetString()
            .Should().Be("Escolha a janela de entrega:\n1. 14/11 09:00-12:00\n2. 14/11 12:00-15:00");
        await menus.Received(1).GuardarAsync(_sessao.Sessao, "5511999999999",
            Arg.Is<IReadOnlyList<(string Id, string Titulo)>>(o => o.SequenceEqual(opcoes)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Falha_do_servidor_nao_repete_envio()
    {
        _http.QuandoQualquer().Responde(HttpStatusCode.InternalServerError);
        var act = () => Client.EnviarTextoAsync("5511999999999", "Olá");
        await act.Should().ThrowAsync<WhatsAppCloudException>().Where(e => e.StatusHttp == 500);
        _http.Chamadas.Should().HaveCount(1);
    }

    // Risco conhecido #2071 (caracterização, não correção): fora da janela de 24 h o domínio pede modelo, e a
    // conexão provisória recusa de forma permanente em vez de enviar texto. Quem corrigir inverte esta asserção.
    [Fact]
    public async Task Risco_modelo_fora_da_janela_falha_permanente_sem_sair_como_texto()
    {
        var act = () => Client.EnviarTemplateAsync("5511999999999", "aviso_pedido", "pt_BR", ["1"]);
        await act.Should().ThrowAsync<WhatsAppCloudException>().Where(e => e.EhPermanente);
        _http.Chamadas.Should().BeEmpty();
    }

    [Fact]
    public async Task Sem_sessao_do_tenant_nao_chama_o_servidor()
    {
        _remetente.ObterSessaoWebAsync(Arg.Any<CancellationToken>()).Returns((CredencialWppConnect?)null);
        var act = () => Client.EnviarTextoAsync("5511999999999", "Olá");
        await act.Should().ThrowAsync<WhatsAppCloudException>();
        _http.Chamadas.Should().BeEmpty();
    }

    [Fact]
    public async Task Midia_baixada_pela_sessao_atual_e_nao_por_url_do_webhook()
    {
        _http.Quando(HttpMethod.Post, $"/api/{_sessao.Sessao}/download-media")
            .Responde(HttpStatusCode.OK, """{"base64":"aW1hZ2Vt","mimetype":"image/jpeg"}""");
        var (stream, mime) = await Client.BaixarMidiaAsync("wpp:media-id");
        using var reader = new StreamReader(stream);
        (await reader.ReadToEndAsync()).Should().Be("imagem");
        mime.Should().Be("image/jpeg");
    }

    [Fact]
    public async Task Imagem_usa_o_endpoint_real_de_arquivo()
    {
        _http.Quando(HttpMethod.Post, $"/api/{_sessao.Sessao}/send-file-base64")
            .Responde(HttpStatusCode.Created, """{"status":"success","response":[{"id":"imagem-id"}]}""");
        (await Client.EnviarImagemAsync("5511999999999", "https://storage.test/files/imagem.jpg", "Legenda"))
            .Wamid.Should().Be(_sessao.IdExterno("imagem-id"));
    }

    [Fact]
    public async Task Nota_de_voz_usa_o_endpoint_real_de_audio()
    {
        _http.Quando(HttpMethod.Post, $"/api/{_sessao.Sessao}/send-voice")
            .Responde(HttpStatusCode.Created, """{"status":"success","response":[{"id":"audio-id"}]}""");
        (await Client.EnviarAudioAsync("5511999999999", "https://storage.test/files/audio.ogg", true))
            .Wamid.Should().Be(_sessao.IdExterno("audio-id"));
    }

    [Fact]
    public async Task Estado_em_memoria_nao_basta_para_declarar_conectado()
    {
        _http.Quando(HttpMethod.Get, $"/api/{_sessao.Sessao}/status-session")
            .Responde(HttpStatusCode.OK, """{"status":"CONNECTED"}""");
        _http.Quando(HttpMethod.Get, $"/api/{_sessao.Sessao}/check-connection-session")
            .Responde(HttpStatusCode.OK, """{"status":false}""");
        (await Client.ObterEstadoAsync(_sessao)).Conectado.Should().BeFalse();
    }

    [Fact]
    public async Task Conexao_confirmada_retira_o_QR_residual_do_gateway()
    {
        _http.Quando(HttpMethod.Get, $"/api/{_sessao.Sessao}/status-session")
            .Responde(HttpStatusCode.OK, """{"status":"CONNECTED","qrcode":"data:image/png;base64,cXI="}""");
        _http.Quando(HttpMethod.Get, $"/api/{_sessao.Sessao}/check-connection-session")
            .Responde(HttpStatusCode.OK, """{"status":true}""");
        var estado = await Client.ObterEstadoAsync(_sessao);
        estado.Conectado.Should().BeTrue();
        estado.QrCode.Should().BeNull();
    }

    [Fact]
    public async Task Resolve_LID_pelo_phoneNumber_retornado_pela_versao_instalada()
    {
        _http.Quando(HttpMethod.Get, $"/api/{_sessao.Sessao}/contact/pn-lid/123%40lid")
            .Responde(HttpStatusCode.OK, """{"lid":{"_serialized":"123@lid"},"phoneNumber":{"id":"5511999999999","server":"c.us","_serialized":"5511999999999@c.us"},"contact":null}""");
        (await Client.ResolverTelefoneAsync(_sessao, "123@lid")).Should().Be("5511999999999");
    }

    [Fact]
    public async Task Lista_mensagens_recentes_com_id_em_texto_ou_objeto_e_descarta_as_sem_id()
    {
        // #2590: a mensagem lida depois do evento já traz o id que o webhook perdeu.
        _http.Quando(HttpMethod.Get, $"/api/{_sessao.Sessao}/get-messages/5511999999999%40c.us")
            .Responde(HttpStatusCode.OK, """{"status":"success","response":[{"id":"false_5511999999999@c.us_ABC","t":1793000000,"fromMe":false,"type":"chat","body":"Oi"},{"id":{"_serialized":"true_5511999999999@c.us_DEF"},"t":1793000001,"fromMe":true,"type":"image"},{"t":1793000002,"fromMe":false}]}""");

        var recentes = await Client.ListarMensagensRecentesAsync(_sessao, "5511999999999@c.us");

        recentes.Should().Equal(
            new MensagemRecenteWppConnect("false_5511999999999@c.us_ABC", 1793000000, false, "chat", "Oi"),
            new MensagemRecenteWppConnect("true_5511999999999@c.us_DEF", 1793000001, true, "image", null));
        _http.UltimaChamada!.Uri.Query.Should().Be("?count=30");
    }

    [Fact]
    public async Task Lista_a_ultima_recebida_de_cada_conversa_individual()
    {
        // #3117: grupo, transmissão, conversa sem chave e última recebida da própria loja ficam de fora.
        _http.Quando(HttpMethod.Post, $"/api/{_sessao.Sessao}/list-chats")
            .Responde(HttpStatusCode.OK, """[{"id":{"_serialized":"5511999999999@c.us"},"t":1793000000,"lastReceivedKey":{"fromMe":false,"remote":"5511999999999@c.us","id":"ABC","_serialized":"false_5511999999999@c.us_ABC"}},{"id":"123@lid","lastReceivedKey":{"fromMe":false,"_serialized":"false_123@lid_DEF"}},{"id":{"_serialized":"120363000000000000@g.us"},"lastReceivedKey":{"fromMe":false,"_serialized":"false_120363000000000000@g.us_G"}},{"id":{"_serialized":"status@broadcast"},"lastReceivedKey":{"fromMe":false,"_serialized":"false_status@broadcast_S"}},{"id":{"_serialized":"5511888888888@c.us"}},{"id":{"_serialized":"5511777777777@c.us"},"lastReceivedKey":{"fromMe":true,"_serialized":"true_5511777777777@c.us_X"}},"lixo"]""");

        var ultimas = await Client.ListarUltimasRecebidasAsync(_sessao, 20);

        ultimas.Should().Equal(
            new UltimaRecebidaWppConnect("5511999999999@c.us", "false_5511999999999@c.us_ABC"),
            new UltimaRecebidaWppConnect("123@lid", "false_123@lid_DEF"));
        _http.UltimaChamada!.Cabecalho("Authorization").Should().Be("Bearer token-da-sessao");
    }

    [Theory]
    [InlineData("""{"status":"success","response":[{"id":"5511999999999@c.us","lastReceivedKey":{"_serialized":"false_5511999999999@c.us_ABC"}}]}""", 1)]
    [InlineData("""{"status":"error","message":"Error on get all chats"}""", 0)]
    [InlineData("\"texto\"", 0)]
    public async Task Lista_de_conversas_em_formato_inesperado_nao_inventa_mensagem(string corpo, int esperadas)
    {
        _http.Quando(HttpMethod.Post, $"/api/{_sessao.Sessao}/list-chats").Responde(HttpStatusCode.OK, corpo);

        (await Client.ListarUltimasRecebidasAsync(_sessao, 20)).Should().HaveCount(esperadas);
    }

    /// <summary>
    /// #3138 (i): a ponte real (wppconnect-server 2.10.81) devolve <c>lastReceivedKey</c> sem <c>_serialized</c>:
    /// <c>fromMe</c>, <c>remote</c> (objeto), <c>id</c> e <c>$1</c>. Grupo e mensagem da loja seguem fora (guarda de regressão).
    /// </summary>
    [Fact]
    public async Task Lista_a_ultima_recebida_no_formato_real_da_ponte_sem_serialized()
    {
        const string Individual = """{"id":{"server":"c.us","user":"5511999999999","_serialized":"5511999999999@c.us"},"lastReceivedKey":{"fromMe":false,"remote":{"server":"c.us","user":"5511999999999","_serialized":"5511999999999@c.us"},"id":"ABC","$1":"opaco"}}""";
        const string Lid = """{"id":{"server":"lid","user":"123","_serialized":"123@lid"},"lastReceivedKey":{"fromMe":false,"remote":"123@lid","id":"DEF","$1":"opaco"}}""";
        const string Grupo = """{"id":{"server":"g.us","user":"120363000000000000","_serialized":"120363000000000000@g.us"},"lastReceivedKey":{"fromMe":false,"remote":{"_serialized":"120363000000000000@g.us"},"id":"G","participant":{"_serialized":"5511666666666@c.us"},"$1":"opaco"}}""";
        const string DaLoja = """{"id":{"_serialized":"5511777777777@c.us"},"lastReceivedKey":{"fromMe":true,"remote":{"_serialized":"5511777777777@c.us"},"id":"X","$1":"opaco"}}""";
        _http.Quando(HttpMethod.Post, $"/api/{_sessao.Sessao}/list-chats")
            .Responde(HttpStatusCode.OK, $"[{Individual},{Lid},{Grupo},{DaLoja}]");

        var ultimas = await Client.ListarUltimasRecebidasAsync(_sessao, 20);

        ultimas.Should().Equal(
            new UltimaRecebidaWppConnect("5511999999999@c.us", "false_5511999999999@c.us_ABC"),
            new UltimaRecebidaWppConnect("123@lid", "false_123@lid_DEF"));
        // Forma ESPERADA do evento da mesma mensagem, não medida na ponte real: o webhook lê id._serialized, e o
        // id montado só serve ao vigia se tiver essa forma. A prova na ponte é a conferência 2 do runbook.
        using var evento = System.Text.Json.JsonDocument.Parse(
            """{"event":"onmessage","id":{"fromMe":false,"remote":"5511999999999@c.us","id":"ABC","_serialized":"false_5511999999999@c.us_ABC"}}""");
        ultimas[0].MensagemId.Should().Be(
            EasyStock.Application.UseCases.Atendimento.Webhook.WppConnectWebhookParser.Id(evento.RootElement));
    }

    [Theory]
    [InlineData("""{"remote":{"_serialized":"5511999999999@c.us"},"id":"ABC"}""")]
    [InlineData("""{"fromMe":"false","remote":{"_serialized":"5511999999999@c.us"},"id":"ABC"}""")]
    [InlineData("""{"fromMe":false,"id":"ABC"}""")]
    [InlineData("""{"fromMe":false,"remote":{"server":"c.us","user":"5511999999999"},"id":"ABC"}""")]
    [InlineData("""{"fromMe":false,"remote":{"_serialized":"5511999999999@c.us"},"id":""}""")]
    [InlineData("""{"fromMe":false,"remote":{"_serialized":"5511999999999@c.us"},"$1":"false_5511999999999@c.us_ABC"}""")]
    public async Task Chave_incompleta_no_formato_real_nao_inventa_mensagem(string chave)
    {
        _http.Quando(HttpMethod.Post, $"/api/{_sessao.Sessao}/list-chats")
            .Responde(HttpStatusCode.OK, $$"""[{"id":{"_serialized":"5511999999999@c.us"},"lastReceivedKey":{{chave}}}]""");

        (await Client.ListarUltimasRecebidasAsync(_sessao, 20)).Should().BeEmpty();
    }

    /// <summary>#2833: a reação vai pelo id serializado da mensagem; reação vazia sai como <c>false</c>, que retira.</summary>
    [Fact]
    public async Task Reacao_vai_pelo_id_da_mensagem_e_vazia_retira()
    {
        _http.Quando(HttpMethod.Post, $"/api/{_sessao.Sessao}/react-message")
            .Responde(HttpStatusCode.OK, """{"status":"success","response":{"message":"Reaction sended"}}""");
        var wamid = $"wpp:{_sessao.Sessao}:false_5511999999999@c.us_ABC";

        await Client.ReagirAsync("5511999999999", wamid, "👍");
        var corpo = System.Text.Json.JsonDocument.Parse(_http.UltimaChamada!.Corpo!).RootElement;
        corpo.GetProperty("msgId").GetString().Should().Be("false_5511999999999@c.us_ABC");
        corpo.GetProperty("reaction").GetString().Should().Be("👍");

        await Client.ReagirAsync("5511999999999", wamid, null);
        System.Text.Json.JsonDocument.Parse(_http.UltimaChamada!.Corpo!).RootElement
            .GetProperty("reaction").ValueKind.Should().Be(System.Text.Json.JsonValueKind.False);
    }

    /// <summary>#2833: a rota de apagar do WPPConnect não completa o telefone; vai a conversa inteira, inclusive LID.</summary>
    [Theory]
    [InlineData("true_5511999999999@c.us_ABC", "5511999999999@c.us")]
    [InlineData("true_123456789012345@lid_DEF", "123456789012345@lid")]
    public async Task Apagar_para_todos_manda_a_conversa_completa_e_o_id(string serializado, string conversa)
    {
        _http.Quando(HttpMethod.Post, $"/api/{_sessao.Sessao}/delete-message")
            .Responde(HttpStatusCode.OK, """{"status":"success","response":{"message":"Message deleted"}}""");

        await Client.ApagarParaTodosAsync("5511999999999", $"wpp:{_sessao.Sessao}:{serializado}");

        var corpo = System.Text.Json.JsonDocument.Parse(_http.UltimaChamada!.Corpo!).RootElement;
        corpo.GetProperty("phone").GetString().Should().Be(conversa);
        corpo.GetProperty("messageId").GetString().Should().Be(serializado);
        corpo.GetProperty("isGroup").GetBoolean().Should().BeFalse();
        corpo.TryGetProperty("onlyLocal", out var soLocal).Should().BeTrue();
        soLocal.GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Apagar_sem_confirmacao_da_ponte_falha()
    {
        _http.QuandoQualquer().Responde(HttpStatusCode.Unauthorized,
            """{"status":"error","response":{"message":"Error unknown on delete message"}}""");

        var act = () => Client.ApagarParaTodosAsync("5511999999999", $"wpp:{_sessao.Sessao}:true_5511999999999@c.us_ABC");

        await act.Should().ThrowAsync<WhatsAppCloudException>();
    }

    [Fact]
    public async Task Mensagem_que_saiu_pela_Meta_nao_tem_a_acao_na_ponte()
    {
        var daMeta = "wamid.HBgNNTUxMTk5OTk5OTk5ORUCABEYEjdB";

        await ((Func<Task>)(() => Client.ReagirAsync("5511999999999", daMeta, "👍"))).Should().ThrowAsync<AcaoNaoSuportadaWhatsAppException>();
        await ((Func<Task>)(() => Client.ApagarParaTodosAsync("5511999999999", daMeta))).Should().ThrowAsync<AcaoNaoSuportadaWhatsAppException>();
        _http.Chamadas.Should().BeEmpty();
    }

    [Fact]
    public async Task Mensagem_de_outra_sessao_nao_e_apagada_nem_reagida()
    {
        var alheia = "wpp:ez-00000000000000000000000000000000:true_5511999999999@c.us_ABC";

        await ((Func<Task>)(() => Client.ApagarParaTodosAsync("5511999999999", alheia))).Should().ThrowAsync<WhatsAppCloudException>();
        await ((Func<Task>)(() => Client.ReagirAsync("5511999999999", alheia, "👍"))).Should().ThrowAsync<WhatsAppCloudException>();
        _http.Chamadas.Should().BeEmpty();
    }

    /// <summary>#2833: o roteador repassa reagir e apagar ao cliente da sessão, sem cair no padrão "não suporta".</summary>
    [Fact]
    public async Task Roteador_repassa_reagir_e_apagar_a_ponte()
    {
        _http.Quando(HttpMethod.Post, $"/api/{_sessao.Sessao}/react-message")
            .Responde(HttpStatusCode.OK, """{"status":"success","response":{}}""");
        _http.Quando(HttpMethod.Post, $"/api/{_sessao.Sessao}/delete-message")
            .Responde(HttpStatusCode.OK, """{"status":"success","response":{}}""");
        var roteador = new RoteadorWhatsAppClient(_remetente, Client, null!, new ConfigurationBuilder().Build());
        var wamid = $"wpp:{_sessao.Sessao}:true_5511999999999@c.us_ABC";

        await roteador.ReagirAsync("5511999999999", wamid, "❤️");
        await roteador.ApagarParaTodosAsync("5511999999999", wamid);

        _http.Chamadas.Should().HaveCount(2);
    }

    [Fact]
    public async Task Piloto_sem_sessao_nao_cai_em_envio_simulado()
    {
        _remetente.ObterSessaoWebAsync(Arg.Any<CancellationToken>()).Returns((CredencialWppConnect?)null);
        var roteador = new RoteadorWhatsAppClient(_remetente, Client, null!, new ConfigurationBuilder().Build());
        var act = () => roteador.EnviarTextoAsync("5511999999999", "Olá");
        await act.Should().ThrowAsync<WhatsAppCloudException>();
        _http.Chamadas.Should().BeEmpty();
    }

    // #3229: a recusa e a queda no envio saem em log, sem telefone, texto nem corpo cru (#2590).
    private const string CorpoDeRecusa = """{"status":"error","message":"Error on send message","error":{"name":"Error","message":"falhou para 5511987654321@c.us","code":"E1","to":"5511987654321@c.us","body":"texto-secreto-3229"}}""";

    private sealed class LoggerDeCaptura : ILogger<WppConnectClient>
    {
        public List<(LogLevel Nivel, string Mensagem, Exception? Excecao, Dictionary<string, object?> Estado)> Linhas { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Linhas.Add((logLevel, formatter(state, exception), exception,
                state is IEnumerable<KeyValuePair<string, object?>> pares ? pares.ToDictionary(p => p.Key, p => p.Value) : []));
        public string Tudo => string.Join(Environment.NewLine, Linhas.Select(l => l.Mensagem + "|" + string.Join(";", l.Estado.Select(p => $"{p.Key}={p.Value}"))));
    }

    private WppConnectClient ClientComLog(LoggerDeCaptura log) => new(_http.CriarClient("http://wpp.test/"),
        Options.Create(new WppConnectOptions { Habilitado = true, SecretKey = "segredo-teste", WebhookToken = "token-webhook-teste" }),
        _remetente, logger: log);

    [Fact]
    public async Task Recusa_no_envio_grava_um_warning_com_acao_status_e_detalhe_seguro()
    {
        var log = new LoggerDeCaptura();
        _http.Quando(HttpMethod.Post, $"/api/{_sessao.Sessao}/send-message").Responde(HttpStatusCode.InternalServerError, CorpoDeRecusa);

        var act = () => ClientComLog(log).EnviarTextoAsync("5511987654321", "texto-secreto-3229");

        await act.Should().ThrowAsync<WhatsAppCloudException>().Where(e => e.StatusHttp == 500 && e.Codigo == -2071);
        var aviso = log.Linhas.Should().ContainSingle(l => l.Nivel == LogLevel.Warning).Which;
        aviso.Excecao.Should().BeNull();
        aviso.Estado["Acao"].Should().Be("send-message");
        aviso.Estado["StatusHttp"].Should().Be(500);
        aviso.Mensagem.Should().Contain("Error on send message").And.Contain("E1");
        log.Tudo.Should().NotContain("5511987654321").And.NotContain("@c.us").And.NotContain("texto-secreto-3229").And.NotContain(_sessao.Sessao);
    }

    [Fact]
    public async Task Detalhe_da_recusa_e_truncado_em_160_caracteres()
    {
        var log = new LoggerDeCaptura();
        _http.QuandoQualquer().Responde(HttpStatusCode.BadGateway, $$"""{"message":"{{new string('x', 400)}}"}""");

        var act = () => ClientComLog(log).EnviarTextoAsync("5511987654321", "Olá");

        await act.Should().ThrowAsync<WhatsAppCloudException>();
        ((string)log.Linhas.Single().Estado["Detalhe"]!).Length.Should().BeLessThanOrEqualTo(160);
    }

    [Fact]
    public async Task Recusa_com_corpo_que_nao_e_json_grava_warning_sem_detalhe_e_sem_o_corpo()
    {
        var log = new LoggerDeCaptura();
        _http.QuandoQualquer().Responde(HttpStatusCode.BadGateway, "<html>texto-secreto-3229 5511987654321</html>");

        var act = () => ClientComLog(log).EnviarTextoAsync("5511987654321", "Olá");

        await act.Should().ThrowAsync<WhatsAppCloudException>().Where(e => e.StatusHttp == 502);
        log.Linhas.Should().ContainSingle(l => l.Nivel == LogLevel.Warning);
        log.Tudo.Should().Contain("sem detalhe").And.NotContain("texto-secreto-3229").And.NotContain("5511987654321");
    }

    [Fact]
    public async Task Recusa_em_acao_que_nao_e_envio_nao_grava_warning()
    {
        var log = new LoggerDeCaptura();
        _http.Quando(HttpMethod.Post, $"/api/{_sessao.Sessao}/list-chats").Responde(HttpStatusCode.InternalServerError, CorpoDeRecusa);

        var act = () => ClientComLog(log).ListarUltimasRecebidasAsync(_sessao, 10);

        await act.Should().ThrowAsync<WhatsAppCloudException>().Where(e => e.StatusHttp == 500);
        log.Linhas.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Excecao_de_transporte_no_envio_grava_warning_sem_a_mensagem_da_excecao(bool timeout)
    {
        var log = new LoggerDeCaptura();
        Exception falha = timeout
            ? new TaskCanceledException("timeout para 5511987654321 texto-secreto-3229")
            : new HttpRequestException("conexao recusada 5511987654321 texto-secreto-3229");
        _http.QuandoQualquer().Lanca(falha);

        var act = () => ClientComLog(log).EnviarTextoAsync("5511987654321", "texto-secreto-3229");

        await act.Should().ThrowAsync<Exception>().Where(e => ReferenceEquals(e, falha));
        var aviso = log.Linhas.Should().ContainSingle(l => l.Nivel == LogLevel.Warning).Which;
        aviso.Excecao.Should().BeNull();
        aviso.Estado["Acao"].Should().Be("send-message");
        aviso.Estado["TipoExcecao"].Should().Be(falha.GetType().Name);
        log.Tudo.Should().NotContain("5511987654321").And.NotContain("texto-secreto-3229");
    }

    [Fact]
    public async Task Recusa_no_envio_segue_com_a_mesma_classificacao_de_antes()
    {
        var log = new LoggerDeCaptura();
        _http.QuandoQualquer().Responde(HttpStatusCode.BadRequest, CorpoDeRecusa);

        var act = () => ClientComLog(log).EnviarTextoAsync("5511987654321", "Olá");

        await act.Should().ThrowAsync<WhatsAppCloudException>().Where(e => e.StatusHttp == 400 && e.EhPermanente && e.Codigo == -2071);
    }

    [Fact]
    public async Task Telefone_em_grupos_tambem_e_mascarado_no_detalhe()
    {
        var log = new LoggerDeCaptura();
        _http.QuandoQualquer().Responde(HttpStatusCode.InternalServerError,
            """{"message":"falha para +55 11 987 654 321","error":{"message":"(11) 98765-4321"}}""");

        var act = () => ClientComLog(log).EnviarTextoAsync("5511987654321", "Olá");

        await act.Should().ThrowAsync<WhatsAppCloudException>().Where(e => e.StatusHttp == 500);
        log.Tudo.Should().Contain("falha para").And.NotContain("987").And.NotContain("654").And.NotContain("4321").And.NotContain("(11)");
    }

    [Fact]
    public async Task Corpo_da_recusa_acima_do_limite_nao_troca_a_falha_nem_vai_ao_log()
    {
        var log = new LoggerDeCaptura();
        _http.QuandoQualquer().Responde(HttpStatusCode.InternalServerError, $$"""{"message":"{{new string('x', 70_000)}}"}""");

        var act = () => ClientComLog(log).EnviarTextoAsync("5511987654321", "Olá");

        await act.Should().ThrowAsync<WhatsAppCloudException>().Where(e => e.StatusHttp == 500 && e.Codigo == -2071 && !e.EhPermanente);
        log.Linhas.Should().ContainSingle(l => l.Nivel == LogLevel.Warning);
        log.Tudo.Length.Should().BeLessThan(1_000);
    }
}

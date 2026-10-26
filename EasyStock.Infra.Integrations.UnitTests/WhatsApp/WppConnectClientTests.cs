using System.Net;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Infra.Integrations.WhatsApp;
using EasyStock.TestHelpers.Http;
using FluentAssertions;
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

    [Fact]
    public async Task Falha_do_servidor_nao_repete_envio()
    {
        _http.QuandoQualquer().Responde(HttpStatusCode.InternalServerError);
        var act = () => Client.EnviarTextoAsync("5511999999999", "Olá");
        await act.Should().ThrowAsync<WhatsAppCloudException>().Where(e => e.StatusHttp == 500);
        _http.Chamadas.Should().HaveCount(1);
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
    public async Task Piloto_sem_sessao_nao_cai_em_envio_simulado()
    {
        _remetente.ObterSessaoWebAsync(Arg.Any<CancellationToken>()).Returns((CredencialWppConnect?)null);
        var roteador = new RoteadorWhatsAppClient(_remetente, Client, null!, new ConfigurationBuilder().Build());
        var act = () => roteador.EnviarTextoAsync("5511999999999", "Olá");
        await act.Should().ThrowAsync<WhatsAppCloudException>();
        _http.Chamadas.Should().BeEmpty();
    }
}

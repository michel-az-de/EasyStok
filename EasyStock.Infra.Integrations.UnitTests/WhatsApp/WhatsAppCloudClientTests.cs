using System.Net;
using System.Net.Http.Headers;
using System.Text;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Infra.Integrations.DependencyInjection;
using EasyStock.Infra.Integrations.WhatsApp;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Polly;
using Polly.Registry;

namespace EasyStock.Infra.Integrations.UnitTests.WhatsApp;

/// <summary>
/// HTTP é stubado por <see cref="SequenceHandler"/> — nenhuma chamada de rede real. O pipeline
/// Polly é <see cref="ResiliencePipeline.Empty"/> (no-op): o que estes testes provam é o que o
/// CLIENTE decide (parse, validação, quando lança), não o comportamento de retry em si.
/// </summary>
public class WhatsAppCloudClientTests
{
    private static WhatsAppCloudClient CreateClient(HttpStatusCode status, string body, out SequenceHandler handler)
    {
        handler = new SequenceHandler();
        handler.Enfileirar(status, body);
        return BuildClient(handler);
    }

    private static WhatsAppCloudClient BuildClient(
        SequenceHandler handler, string? phoneNumberIdDoTenant = null, string phoneNumberIdGlobal = "1234567890",
        bool? haTenant = null, ResiliencePipelineProvider<string>? pipelines = null, string? tokenDoTenant = null)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://graph.test/v19.0/") };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token-teste");

        var options = Options.Create(new WhatsAppCloudOptions
        {
            AccessToken = "token-teste",
            PhoneNumberId = phoneNumberIdGlobal,
            BaseUrl = "https://graph.test/v19.0"
        });

        var remetente = Substitute.For<IRemetenteWhatsApp>();
        remetente.ObterPhoneNumberIdAsync(Arg.Any<CancellationToken>()).Returns(phoneNumberIdDoTenant);
        remetente.HaTenantCorrente.Returns(haTenant ?? phoneNumberIdDoTenant is not null);
        remetente.ObterAccessTokenAsync(Arg.Any<CancellationToken>()).Returns(tokenDoTenant);

        var pipelineProvider = pipelines ?? Substitute.For<ResiliencePipelineProvider<string>>();
        if (pipelines is null)
            pipelineProvider.GetPipeline(Arg.Any<string>()).Returns(ResiliencePipeline.Empty);

        return new WhatsAppCloudClient(http, options, remetente, pipelineProvider, NullLogger<WhatsAppCloudClient>.Instance);
    }

    private const string RespostaEnvioOk =
        """{"messaging_product":"whatsapp","contacts":[{"wa_id":"5511999998888"}],"messages":[{"id":"wamid.X"}]}""";

    [Fact]
    public async Task EnviaPeloPhoneNumberIdDaEmpresaDoTenant()
    {
        var handler = new SequenceHandler();
        handler.Enfileirar(HttpStatusCode.OK, RespostaEnvioOk);
        var client = BuildClient(handler, phoneNumberIdDoTenant: "5550001111");

        await client.EnviarTextoAsync("5511999998888", "Oi!");

        handler.UltimaUrl.Should().Be("https://graph.test/v19.0/5550001111/messages",
            "a resposta da empresa X tem que sair pelo número da empresa X, não pelo global");
    }

    [Fact]
    public async Task SemTenantUsaOGlobal()
    {
        var handler = new SequenceHandler();
        handler.Enfileirar(HttpStatusCode.OK, RespostaEnvioOk);
        var client = BuildClient(handler, phoneNumberIdDoTenant: null, haTenant: false);

        await client.EnviarTextoAsync("5511999998888", "Oi!");

        handler.UltimaUrl.Should().Be("https://graph.test/v19.0/1234567890/messages",
            "sem empresa corrente (diagnóstico) o número global ainda serve");
    }

    [Fact]
    public async Task TenantSemNumeroFalhaSemUsarOGlobal()
    {
        // #1292: a empresa B sem número vinculado não pode falar com o cliente dela pelo número da empresa A.
        var handler = new SequenceHandler();
        handler.Enfileirar(HttpStatusCode.OK, RespostaEnvioOk);
        var client = BuildClient(handler, phoneNumberIdDoTenant: null, haTenant: true);

        var act = async () => await client.EnviarTextoAsync("5511999998888", "Oi!");

        var ex = await act.Should().ThrowAsync<WhatsAppCloudException>();
        ex.Which.EhPermanente.Should().BeTrue();
        ex.Which.Message.Should().Contain("phone_number_id");
        handler.Chamadas.Should().Be(0, "o envio não pode sair pelo número global");
    }

    [Fact]
    public async Task PostDeEnvioNaoEhReenviadoQuandoARedeFalha()
    {
        // #1292: POST /messages não é idempotente; repetir depois de um timeout ou queda de conexão
        // pode entregar a mesma mensagem duas vezes ao cliente.
        var handler = new SequenceHandler();
        handler.EnfileirarFalha(new HttpRequestException("conexão caiu depois do envio"));
        handler.Enfileirar(HttpStatusCode.OK, RespostaEnvioOk);
        var client = BuildClient(handler, phoneNumberIdDoTenant: "5550001111", pipelines: PipelinesReais());

        var act = async () => await client.EnviarTextoAsync("5511999998888", "Oi!");

        await act.Should().ThrowAsync<HttpRequestException>();
        handler.Chamadas.Should().Be(1, "o envio só pode ir à Meta uma vez");
    }

    [Fact]
    public async Task GetDeMidiaContinuaComRetry()
    {
        var handler = new SequenceHandler();
        handler.EnfileirarFalha(new HttpRequestException("falha transitória"));
        handler.Enfileirar(HttpStatusCode.OK,
            """{"url":"https://graph.test/media-cdn/abc","mime_type":"image/jpeg","id":"media-1"}""");
        handler.Enfileirar(HttpStatusCode.OK, "binario-fake");
        var client = BuildClient(handler, phoneNumberIdDoTenant: "5550001111", pipelines: PipelinesReais());

        var (_, mime) = await client.BaixarMidiaAsync("media-1");

        mime.Should().Be("image/jpeg");
        handler.Chamadas.Should().Be(3, "GET é idempotente: a falha transitória é repetida");
    }

    private static ResiliencePipelineProvider<string> PipelinesReais()
    {
        var services = new ServiceCollection();
        services.AddEasyStockIntegrationResilience();
        return services.BuildServiceProvider().GetRequiredService<ResiliencePipelineProvider<string>>();
    }

    [Fact]
    public async Task MarcarComoLidaTambemUsaONumeroDoTenant()
    {
        var handler = new SequenceHandler();
        handler.Enfileirar(HttpStatusCode.OK, """{"success":true}""");
        var client = BuildClient(handler, phoneNumberIdDoTenant: "5550001111");

        await client.MarcarComoLidaAsync("wamid.X");

        handler.UltimaUrl.Should().Be("https://graph.test/v19.0/5550001111/messages");
    }

    [Fact]
    public async Task SemNenhumNumeroLancaErroClaroSemChamarARede()
    {
        var handler = new SequenceHandler();
        var client = BuildClient(handler, phoneNumberIdDoTenant: null, phoneNumberIdGlobal: "", haTenant: false);

        var act = async () => await client.EnviarTextoAsync("5511999998888", "Oi!");

        var ex = await act.Should().ThrowAsync<WhatsAppCloudException>();
        ex.Which.Message.Should().Contain("phone_number_id");
        ex.Which.EhPermanente.Should().BeTrue();
        handler.Chamadas.Should().Be(0, "sem número não pode haver POST em \"/messages\"");
    }

    [Fact]
    public async Task EnviaTextoEDevolveWamid()
    {
        const string json = """{"messaging_product":"whatsapp","contacts":[{"wa_id":"5511999998888"}],"messages":[{"id":"wamid.HBgLNTUx"}]}""";
        var client = CreateClient(HttpStatusCode.OK, json, out var handler);

        var resultado = await client.EnviarTextoAsync("5511999998888", "Oi!");

        resultado.Wamid.Should().Be("wamid.HBgLNTUx");
        handler.UltimoCorpo.Should().Contain("\"messaging_product\":\"whatsapp\"");
        handler.UltimoCorpo.Should().Contain("\"to\":\"5511999998888\"");
        handler.UltimoCorpo.Should().Contain("\"type\":\"text\"");
        handler.UltimoCorpo.Should().Contain("\"body\":\"Oi!\"");
    }

    [Fact]
    public async Task BotoesValidaLimites()
    {
        var client = CreateClient(HttpStatusCode.OK, "{}", out var handler);

        (string, string)[] quatroBotoes = [("a", "1"), ("b", "2"), ("c", "3"), ("d", "4")];
        var act1 = async () => await client.EnviarBotoesAsync("5511999998888", "corpo", quatroBotoes);
        await act1.Should().ThrowAsync<ArgumentException>();

        (string, string)[] tituloLongo = [("a", new string('x', 21))];
        var act2 = async () => await client.EnviarBotoesAsync("5511999998888", "corpo", tituloLongo);
        await act2.Should().ThrowAsync<ArgumentException>();

        handler.Chamadas.Should().Be(0, "a validação precisa acontecer antes de qualquer chamada de rede");
    }

    [Fact]
    public async Task Erro131047EhPermanenteSemRetry()
    {
        const string json = """{"error":{"message":"Message failed to send because more than 24 hours have passed since the customer last replied to this number.","type":"OAuthException","code":131047,"error_subcode":2494010,"fbtrace_id":"Abc123"}}""";
        var client = CreateClient(HttpStatusCode.BadRequest, json, out var handler);

        var act = async () => await client.EnviarTextoAsync("5511999998888", "Oi!");

        var ex = await act.Should().ThrowAsync<WhatsAppCloudException>();
        ex.Which.Codigo.Should().Be(131047);
        ex.Which.EhPermanente.Should().BeTrue();
        handler.Chamadas.Should().Be(1, "erro permanente não pode ser reenviado — o pipeline nem vê essa exceção");
    }

    [Fact]
    public async Task Erro132001EhPermanenteSemRetry()
    {
        // N6: a tabela de códigos é uma só para a loja e a plataforma; 132001 (template não existe) nunca passa.
        const string json = """{"error":{"message":"Template name does not exist in the translation","type":"OAuthException","code":132001,"fbtrace_id":"X"}}""";
        var client = CreateClient(HttpStatusCode.BadRequest, json, out var handler);

        var act = async () => await client.EnviarTextoAsync("5511999998888", "Oi!");

        var ex = await act.Should().ThrowAsync<WhatsAppCloudException>();
        ex.Which.Codigo.Should().Be(132001);
        ex.Which.EhPermanente.Should().BeTrue();
        handler.Chamadas.Should().Be(1);
    }

    [Fact]
    public async Task Erro131056NaoEPermanente()
    {
        const string json = """{"error":{"message":"Pair rate limit hit","type":"OAuthException","code":131056,"fbtrace_id":"X"}}""";
        var client = CreateClient(HttpStatusCode.BadRequest, json, out _);

        var act = async () => await client.EnviarTextoAsync("5511999998888", "Oi!");

        var ex = await act.Should().ThrowAsync<WhatsAppCloudException>();
        ex.Which.EhPermanente.Should().BeFalse();
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, 400)]
    [InlineData(HttpStatusCode.TooManyRequests, 429)]
    [InlineData(HttpStatusCode.ServiceUnavailable, 503)]
    public async Task ErroDaMetaCarregaOStatusHttp(HttpStatusCode status, int esperado)
    {
        // N2: o provider do outbox separa o 5xx (Indeterminado: a Meta pode ter aceitado) da recusa 4xx (nada saiu).
        const string json = """{"error":{"message":"An unknown error occurred","type":"OAuthException","code":2,"fbtrace_id":"X"}}""";
        var client = CreateClient(status, json, out _);

        var act = async () => await client.EnviarTextoAsync("5511999998888", "Oi!");

        var ex = await act.Should().ThrowAsync<WhatsAppCloudException>();
        ex.Which.StatusHttp.Should().Be(esperado);
        ex.Which.Codigo.Should().Be(2);
    }

    [Fact]
    public async Task ErroSemCorpoDaMetaTambemCarregaOStatusHttp()
    {
        var client = CreateClient(HttpStatusCode.BadGateway, "<html>bad gateway</html>", out _);

        var act = async () => await client.EnviarTextoAsync("5511999998888", "Oi!");

        var ex = await act.Should().ThrowAsync<WhatsAppCloudException>();
        ex.Which.StatusHttp.Should().Be(502);
        ex.Which.Codigo.Should().Be(0);
    }

    [Fact]
    public async Task BaixaMidiaComBearer()
    {
        var handler = new SequenceHandler();
        handler.Enfileirar(HttpStatusCode.OK,
            """{"url":"https://graph.test/media-cdn/abc","mime_type":"image/jpeg","id":"media-1"}""");
        handler.Enfileirar(HttpStatusCode.OK, "binario-fake");
        var client = BuildClient(handler);

        var (conteudo, mime) = await client.BaixarMidiaAsync("media-1");
        using var reader = new StreamReader(conteudo);
        var texto = await reader.ReadToEndAsync();

        mime.Should().Be("image/jpeg");
        texto.Should().Be("binario-fake");
        handler.Chamadas.Should().Be(2, "metadados + binário são duas chamadas");
        handler.TodosComBearer.Should().BeTrue();
    }

    [Fact]
    public async Task TemplateComImagemMandaOCabecalhoAntesDoCorpo()
    {
        // #1226: template com cabeçalho IMAGE exige o parâmetro de cabeçalho no envio.
        var client = CreateClient(HttpStatusCode.OK, RespostaEnvioOk, out var handler);

        await client.EnviarTemplateAsync("5511999998888", "campanha_generica", "pt_BR", ["Ana", "Oi"],
            imagemCabecalho: "https://cdn.test/arte.jpg");

        var json = System.Text.Json.JsonDocument.Parse(handler.UltimoCorpo!).RootElement;
        var componentes = json.GetProperty("template").GetProperty("components");
        componentes[0].GetProperty("type").GetString().Should().Be("header");
        var parametro = componentes[0].GetProperty("parameters")[0];
        parametro.GetProperty("type").GetString().Should().Be("image");
        parametro.GetProperty("image").GetProperty("link").GetString().Should().Be("https://cdn.test/arte.jpg");
        componentes[1].GetProperty("type").GetString().Should().Be("body");
    }

    [Fact]
    public async Task TemplateSemImagemNaoTemCabecalho()
    {
        var client = CreateClient(HttpStatusCode.OK, RespostaEnvioOk, out var handler);

        await client.EnviarTemplateAsync("5511999998888", "pedido_pago", "pt_BR", ["#123"]);

        handler.UltimoCorpo.Should().NotContain("\"header\"");
    }

    [Fact]
    public async Task EmpresaComCredencialEnviaComOTokenDela()
    {
        // #1417: coexistência. O número da loja só aceita o business token da empresa.
        var handler = new SequenceHandler();
        handler.Enfileirar(HttpStatusCode.OK, RespostaEnvioOk);
        handler.Enfileirar(HttpStatusCode.OK, "{}");
        var client = BuildClient(handler, phoneNumberIdDoTenant: "5550001111", tokenDoTenant: "token-da-empresa");

        await client.EnviarTextoAsync("5511999998888", "Oi!");
        await client.MarcarComoLidaAsync("wamid.X");

        handler.Tokens.Should().Equal("token-da-empresa", "token-da-empresa");
    }

    [Fact]
    public async Task EmpresaSemCredencialMantemOTokenGlobal()
    {
        var handler = new SequenceHandler();
        handler.Enfileirar(HttpStatusCode.OK, RespostaEnvioOk);
        var client = BuildClient(handler, phoneNumberIdDoTenant: "5550001111", tokenDoTenant: null);

        await client.EnviarTextoAsync("5511999998888", "Oi!");

        handler.Tokens.Should().Equal("token-teste");
    }

    [Fact]
    public async Task BaixaMidiaComOTokenDaEmpresaNasDuasChamadas()
    {
        var handler = new SequenceHandler();
        handler.Enfileirar(HttpStatusCode.OK,
            """{"url":"https://graph.test/media-cdn/abc","mime_type":"audio/ogg","id":"media-1"}""");
        handler.Enfileirar(HttpStatusCode.OK, "binario-fake");
        var client = BuildClient(handler, phoneNumberIdDoTenant: "5550001111", tokenDoTenant: "token-da-empresa");

        await client.BaixarMidiaAsync("media-1");

        handler.Tokens.Should().Equal("token-da-empresa", "token-da-empresa");
    }

    [Fact]
    public async Task EnvioDePlataformaIgnoraOTokenDaEmpresa()
    {
        // O número de plataforma (N6) é do app: continua no token global mesmo com tenant conectado.
        var handler = new SequenceHandler();
        handler.Enfileirar(HttpStatusCode.OK, RespostaEnvioOk);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://graph.test/v19.0/") };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token-teste");
        var remetente = Substitute.For<IRemetenteWhatsApp>();
        remetente.ObterAccessTokenAsync(Arg.Any<CancellationToken>()).Returns("token-da-empresa");
        var pipelines = Substitute.For<ResiliencePipelineProvider<string>>();
        pipelines.GetPipeline(Arg.Any<string>()).Returns(ResiliencePipeline.Empty);
        var client = new WhatsAppCloudClient(http,
            Options.Create(new WhatsAppCloudOptions { AccessToken = "token-teste", PhoneNumberIdPlataforma = "7770001" }),
            remetente, pipelines, NullLogger<WhatsAppCloudClient>.Instance);

        await client.EnviarTextoPlataformaAsync("5511999998888", "Oi!");

        handler.Tokens.Should().Equal("token-teste");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AudioSaiComoTipoAudioPeloLinkEMarcaNotaDeVoz(bool notaDeVoz)
    {
        // #1444: a Meta busca o áudio pela URL; "voice" só vale para Ogg/Opus mono.
        var client = CreateClient(HttpStatusCode.OK, RespostaEnvioOk, out var handler);

        var envio = await client.EnviarAudioAsync("5511999998888", "https://cdn.test/a.ogg", notaDeVoz);

        envio.Wamid.Should().Be("wamid.X");
        using var json = System.Text.Json.JsonDocument.Parse(handler.UltimoCorpo!);
        json.RootElement.GetProperty("type").GetString().Should().Be("audio");
        var audio = json.RootElement.GetProperty("audio");
        audio.GetProperty("link").GetString().Should().Be("https://cdn.test/a.ogg");
        audio.GetProperty("voice").GetBoolean().Should().Be(notaDeVoz);
    }

    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body, Exception? Falha)> _respostas = new();

        /// <summary>Bearer de cada chamada, na ordem (#1417).</summary>
        public List<string?> Tokens { get; } = [];

        public int Chamadas { get; private set; }
        public string? UltimoCorpo { get; private set; }
        public string? UltimaUrl { get; private set; }
        public bool TodosComBearer { get; private set; } = true;

        public void Enfileirar(HttpStatusCode status, string body) => _respostas.Enqueue((status, body, null));

        public void EnfileirarFalha(Exception falha) => _respostas.Enqueue((default, string.Empty, falha));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Chamadas++;
            UltimaUrl = request.RequestUri?.ToString();
            Tokens.Add(request.Headers.Authorization?.Parameter);
            if (request.Headers.Authorization is not { Scheme: "Bearer" })
                TodosComBearer = false;

            if (request.Content is not null)
                UltimoCorpo = await request.Content.ReadAsStringAsync(cancellationToken);

            var (status, body, falha) = _respostas.Count > 0 ? _respostas.Dequeue() : (HttpStatusCode.OK, "{}", null);
            if (falha is not null) throw falha;
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }
}

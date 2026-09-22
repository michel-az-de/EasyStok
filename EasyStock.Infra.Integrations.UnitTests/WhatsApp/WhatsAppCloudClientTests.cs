using System.Net;
using System.Net.Http.Headers;
using System.Text;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Infra.Integrations.WhatsApp;
using FluentAssertions;
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

    private static WhatsAppCloudClient BuildClient(SequenceHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://graph.test/v19.0/") };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token-teste");

        var options = Options.Create(new WhatsAppCloudOptions
        {
            AccessToken = "token-teste",
            PhoneNumberId = "1234567890",
            BaseUrl = "https://graph.test/v19.0"
        });

        var pipelineProvider = Substitute.For<ResiliencePipelineProvider<string>>();
        pipelineProvider.GetPipeline(Arg.Any<string>()).Returns(ResiliencePipeline.Empty);

        return new WhatsAppCloudClient(http, options, pipelineProvider, NullLogger<WhatsAppCloudClient>.Instance);
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

    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> _respostas = new();

        public int Chamadas { get; private set; }
        public string? UltimoCorpo { get; private set; }
        public bool TodosComBearer { get; private set; } = true;

        public void Enfileirar(HttpStatusCode status, string body) => _respostas.Enqueue((status, body));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Chamadas++;
            if (request.Headers.Authorization is not { Scheme: "Bearer" })
                TodosComBearer = false;

            if (request.Content is not null)
                UltimoCorpo = await request.Content.ReadAsStringAsync(cancellationToken);

            var (status, body) = _respostas.Count > 0 ? _respostas.Dequeue() : (HttpStatusCode.OK, "{}");
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }
}

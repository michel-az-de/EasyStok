using System.Net;
using System.Text;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Infra.Integrations.WhatsApp.Waha;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Polly;
using Polly.Registry;

namespace EasyStock.Infra.Integrations.UnitTests.WhatsApp.Waha;

/// <summary>WAHA de mentira: responde por rota e guarda cada chamada (método, caminho, corpo e chave da API).</summary>
public sealed class WahaFalso : HttpMessageHandler
{
    public const string NumeroDaLoja = "5541999990000";
    public const string TelefoneDoCliente = "5541988887777";

    private readonly Dictionary<string, Queue<(HttpStatusCode Status, string Corpo, string? Tipo)>> _respostas = new(StringComparer.Ordinal);

    public List<(string Metodo, string Caminho, string Corpo, string? Chave)> Chamadas { get; } = [];

    /// <summary>Rotas que não respondem até a chamada ser cancelada (o WAHA espera a sessão voltar).</summary>
    public HashSet<string> Penduradas { get; } = new(StringComparer.Ordinal);

    public void Responder(string caminho, HttpStatusCode status, string corpo, string? tipo = "application/json")
    {
        if (!_respostas.TryGetValue(caminho, out var fila)) _respostas[caminho] = fila = new();
        fila.Enqueue((status, corpo, tipo));
    }

    public IEnumerable<(string Metodo, string Caminho, string Corpo, string? Chave)> Em(string caminho) =>
        Chamadas.Where(c => c.Caminho == caminho);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var caminho = request.RequestUri!.AbsolutePath.TrimStart('/');
        var corpo = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var chave = request.Headers.TryGetValues("X-Api-Key", out var v) ? v.FirstOrDefault() : null;
        Chamadas.Add((request.Method.Method, Uri.UnescapeDataString(caminho), corpo, chave));
        if (Penduradas.Contains(caminho)) await Task.Delay(Timeout.Infinite, cancellationToken);

        if (_respostas.TryGetValue(Uri.UnescapeDataString(caminho), out var fila) && fila.Count > 0)
        {
            var (status, conteudo, tipo) = fila.Dequeue();
            var resposta = new HttpResponseMessage(status) { Content = new StringContent(conteudo, Encoding.UTF8) };
            if (tipo is not null) resposta.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(tipo);
            return resposta;
        }

        // Envio sem resposta configurada: o WAHA devolve 201 com o id na forma {deMim}_{conversa}_{id}.
        if (caminho.StartsWith("api/send", StringComparison.Ordinal))
        {
            var conversa = System.Text.Json.JsonDocument.Parse(corpo).RootElement.GetProperty("chatId").GetString();
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("{\"id\":\"true_" + conversa + "_3EB0AAA111\",\"_data\":{}}", Encoding.UTF8, "application/json"),
            };
        }

        return new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
    }

    public static IOptions<WahaOptions> Opcoes(Action<WahaOptions>? ajuste = null)
    {
        var opcoes = new WahaOptions
        {
            Ligada = true,
            BaseUrl = "http://waha.test:3000",
            ApiKey = "chave-teste",
            HmacKey = "segredo-hmac-de-teste-com-mais-de-32-caracteres",
            Sessao = "default",
            PhoneNumberId = NumeroDaLoja,
            PausaMinimaMs = 0,
            PausaMaximaMs = 0,
            PausaPorCaractereMs = 0,
        };
        ajuste?.Invoke(opcoes);
        return Options.Create(opcoes);
    }

    public static IDistributedCache Cache() => new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

    public HttpClient Http() => new(this) { BaseAddress = new Uri("http://waha.test:3000/") };

    public ResolvedorContatoWaha Resolvedor(IDistributedCache cache, IOptions<WahaOptions> opcoes) =>
        new(Http(), opcoes, cache, NullLogger<ResolvedorContatoWaha>.Instance);

    public static MenuNumeradoWaha Menus(IDistributedCache cache, IOptions<WahaOptions> opcoes) =>
        new(cache, opcoes, NullLogger<MenuNumeradoWaha>.Instance);

    public WahaWhatsAppClient Cliente(
        IDistributedCache? cache = null, IOptions<WahaOptions>? opcoes = null, string? numeroDaEmpresa = NumeroDaLoja, bool haTenant = true,
        EasyStock.Infra.Integrations.WhatsApp.BaixadorFotoPerfilWhatsApp? fotos = null)
    {
        cache ??= Cache();
        opcoes ??= Opcoes();
        var remetente = Substitute.For<IRemetenteWhatsApp>();
        remetente.HaTenantCorrente.Returns(haTenant);
        remetente.ObterPhoneNumberIdAsync(Arg.Any<CancellationToken>()).Returns(numeroDaEmpresa);

        var pipelines = Substitute.For<ResiliencePipelineProvider<string>>();
        pipelines.GetPipeline(Arg.Any<string>()).Returns(ResiliencePipeline.Empty);

        return new WahaWhatsAppClient(Http(), opcoes, remetente, Resolvedor(cache, opcoes), Menus(cache, opcoes), pipelines,
            TimeProvider.System, NullLogger<WahaWhatsAppClient>.Instance, fotos);
    }
}

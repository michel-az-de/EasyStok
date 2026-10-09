using System.Net;
using System.Text;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Infra.Postgre.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Api.UnitTests.Services;

public class GeradorAutoPreenchimentoTests
{
    [Theory]
    [InlineData("OpenAI", true)]
    [InlineData("OpenAI", false)]
    [InlineData("Anthropic", true)]
    [InlineData("Anthropic", false)]
    public async Task Stream_le_apenas_de_forma_assincrona_e_preserva_texto_entre_eventos_invalidos(string provedor, bool temDone)
    {
        var evento = provedor == "OpenAI"
            ? "{\"choices\":[{\"delta\":{\"content\":\"Pão fresco\"}}]}"
            : "{\"type\":\"content_block_delta\",\"delta\":{\"text\":\"Pão fresco\"}}";
        var final = temDone ? $"data: [DONE]\n\ndata: {evento}\n" : "";
        using var stream = new StreamAssincrono($": keep-alive\n\ndata: invalido\n\ndata: {evento}\n\n{final}");
        using var provider = CriarProvider(provedor, new Handler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) })));

        var partes = await GerarAsync(provider);

        partes.Should().Equal("Pão fresco");
        stream.Descartado.Should().BeTrue();
    }

    [Theory]
    [InlineData("OpenAI")]
    [InlineData("Anthropic")]
    public async Task Erro_http_descarta_resposta_antes_de_retornar_fallback(string provedor)
    {
        using var stream = new StreamAssincrono("serviço indisponível");
        using var resposta = new RespostaRastreada(HttpStatusCode.ServiceUnavailable) { Content = new StreamContent(stream) };
        using var provider = CriarProvider(provedor, new Handler((_, _) => Task.FromResult<HttpResponseMessage>(resposta)));

        var partes = await GerarAsync(provider);

        partes.Should().Equal("Descrição não disponível para: Pão.");
        resposta.Descartada.Should().BeTrue();
        stream.Descartado.Should().BeTrue("a resposta de erro também pertence ao gerador");
    }

    [Theory]
    [InlineData("OpenAI")]
    [InlineData("Anthropic")]
    public async Task Cancelamento_da_requisicao_nao_vira_descricao_de_fallback(string provedor)
    {
        using var cts = new CancellationTokenSource();
        using var provider = CriarProvider(provedor, new Handler((_, ct) =>
        {
            cts.Cancel();
            return Task.FromCanceled<HttpResponseMessage>(ct);
        }));

        var act = () => GerarAsync(provider, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData("OpenAI")]
    [InlineData("Anthropic")]
    public async Task Cancelamento_interrompe_leitura_pendente_e_descarta_resposta(string provedor)
    {
        using var cts = new CancellationTokenSource();
        using var stream = new StreamAssincrono(null);
        using var provider = CriarProvider(provedor, new Handler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) })));
        var geracao = GerarAsync(provider, cts.Token);
        await Task.WhenAny(stream.LeituraIniciada.Task, geracao).WaitAsync(TimeSpan.FromSeconds(10));
        cts.Cancel();

        var act = () => geracao.WaitAsync(TimeSpan.FromSeconds(10));

        await act.Should().ThrowAsync<OperationCanceledException>();
        stream.Descartado.Should().BeTrue();
    }

    [Theory]
    [InlineData("OpenAI")]
    [InlineData("Anthropic")]
    public async Task Timeout_do_provedor_preserva_fallback_quando_cliente_nao_cancelou(string provedor)
    {
        using var provider = CriarProvider(provedor, new Handler((_, _) =>
            Task.FromException<HttpResponseMessage>(new TaskCanceledException("Timeout do provedor"))));

        var partes = await GerarAsync(provider);

        partes.Should().Equal("Descrição não disponível para: Pão.");
    }

    [Fact]
    public async Task Cancelamento_ao_ler_erro_Anthropic_descarta_resposta_sem_retornar_fallback()
    {
        using var cts = new CancellationTokenSource();
        using var stream = new StreamAssincrono(null);
        using var resposta = new RespostaRastreada(HttpStatusCode.ServiceUnavailable) { Content = new StreamContent(stream) };
        using var provider = CriarProvider("Anthropic", new Handler((_, _) => Task.FromResult<HttpResponseMessage>(resposta)));
        var geracao = GerarAsync(provider, cts.Token);
        await Task.WhenAny(stream.LeituraIniciada.Task, geracao).WaitAsync(TimeSpan.FromSeconds(10));
        cts.Cancel();

        var act = () => geracao.WaitAsync(TimeSpan.FromSeconds(10));

        await act.Should().ThrowAsync<OperationCanceledException>();
        resposta.Descartada.Should().BeTrue();
        stream.Descartado.Should().BeTrue();
    }

    private static ServiceProvider CriarProvider(string provedor, HttpMessageHandler handler)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{provedor}:Enabled"] = "true",
            [$"{provedor}:ApiKey"] = "chave-ficticia-teste",
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddEasyStockPostgreInfrastructure("Host=localhost;Database=nao_utilizado;Username=postgres", configuration);
        services.AddHttpClient(provedor).ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider();
    }

    private static async Task<List<string>> GerarAsync(ServiceProvider provider, CancellationToken ct = default)
    {
        using var scope = provider.CreateScope();
        var gerador = scope.ServiceProvider.GetRequiredService<IGeradorAutoPreenchimento>();
        var partes = new List<string>();
        await foreach (var parte in gerador.GerarDescricaoProdutoStreamAsync("Pão", null, null, null, ct))
            partes.Add(parte);
        return partes;
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> enviar) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => enviar(request, cancellationToken);
    }

    private sealed class RespostaRastreada(HttpStatusCode status) : HttpResponseMessage(status)
    {
        public bool Descartada { get; private set; }
        protected override void Dispose(bool disposing)
        {
            Descartada = true;
            base.Dispose(disposing);
        }
    }

    private sealed class StreamAssincrono(string? texto) : Stream
    {
        private readonly MemoryStream _dados = new(Encoding.UTF8.GetBytes(texto ?? ""));
        public TaskCompletionSource LeituraIniciada { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Descartado { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Leitura síncrona bloqueia o stream.");
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            LeituraIniciada.TrySetResult();
            if (texto is null)
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return await _dados.ReadAsync(buffer, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            Descartado = true;
            if (disposing) _dados.Dispose();
            base.Dispose(disposing);
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

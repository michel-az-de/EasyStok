using System.Net;
using System.Text;
using EasyStock.Web.Controllers;
using EasyStock.Web.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Web.UnitTests.Controllers;

public class AutoPreenchimentoControllerTests
{
    [Fact]
    public async Task Repassa_eventos_sem_leitura_sincrona_e_fecha_stream_ao_terminar()
    {
        const string eventos = "data: {\"texto\":\"Pão\"}\n\ndata: [DONE]\n\n";
        using var stream = new StreamAssincrono(eventos);
        using var http = new HttpClient(new Handler(stream)) { BaseAddress = new Uri("http://api.test/") };
        using var saida = new MemoryStream();
        var controller = CriarController(http, saida);

        await controller.CompletarProduto("Pão", null, null, null);

        Encoding.UTF8.GetString(saida.ToArray()).Should().Be(eventos);
        stream.Descartado.Should().BeTrue();
    }

    [Fact]
    public async Task Desconexao_do_cliente_cancela_leitura_pendente_e_fecha_stream()
    {
        using var cts = new CancellationTokenSource();
        using var stream = new StreamAssincrono(null);
        using var http = new HttpClient(new Handler(stream)) { BaseAddress = new Uri("http://api.test/") };
        using var saida = new MemoryStream();
        var controller = CriarController(http, saida);
        controller.HttpContext.RequestAborted = cts.Token;

        var execucao = controller.CompletarProduto("Pão", null, null, null);
        await Task.WhenAny(stream.LeituraIniciada.Task, execucao).WaitAsync(TimeSpan.FromSeconds(10));
        cts.Cancel();
        await execucao.WaitAsync(TimeSpan.FromSeconds(10));

        stream.Descartado.Should().BeTrue();
        saida.Length.Should().Be(0);
    }

    private static AutoPreenchimentoController CriarController(HttpClient http, Stream saida)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = saida;
        var api = new ApiClient(http, NullLogger<ApiClient>.Instance);
        return new AutoPreenchimentoController(new AutoPreenchimentoService(api),
            new SessionService(new HttpContextAccessor { HttpContext = context }))
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
    }

    private sealed class Handler(Stream stream) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) });
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

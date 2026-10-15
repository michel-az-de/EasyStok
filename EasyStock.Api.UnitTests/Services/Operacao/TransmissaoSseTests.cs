using System.Text;
using EasyStock.Api.Services.Operacao;
using EasyStock.TestHelpers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Api.UnitTests.Services.Operacao;

/// <summary>
/// #1352 (N7): o SSE de operação sobrevive ao token se o laço só sair por cancelamento. Agora ele fecha no
/// <c>exp</c> do JWT (sem tolerância) e quando o corte de sessão do usuário o alcança, conferido a cada
/// heartbeat mesmo com a fila cheia de eventos. O cliente reconecta e recebe 401.
/// </summary>
public class TransmissaoSseTests
{
    private static readonly TimeSpan Limite = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Intervalo = TimeSpan.FromMilliseconds(50);
    private static readonly DateTimeOffset Inicio = new(2026, 10, 2, 13, 0, 0, TimeSpan.Zero);

    private readonly OperacaoEventBroker _broker = new(NullLogger<OperacaoEventBroker>.Instance);
    private readonly Guid _empresaId = Guid.NewGuid();

    [Fact]
    public async Task FechaNoExp()
    {
        var relogio = new FakeTimeProvider(Inicio);
        using var ouvinte = _broker.SubscribeOperacao("exp", _empresaId);
        var (resposta, corpo) = Resposta();
        using var cts = new CancellationTokenSource(Limite);

        var stream = TransmissaoSse.TransmitirAsync(
            resposta, ouvinte.Slot, Intervalo, Inicio.AddMinutes(30), _ => Task.FromResult(true), relogio, cts.Token);
        await Aguardar(() => corpo.Texto().StartsWith("event: ready", StringComparison.Ordinal));
        stream.IsCompleted.Should().BeFalse("o token ainda não venceu");

        relogio.Advance(TimeSpan.FromMinutes(30)); // exatamente o exp: sem tolerância
        await stream.WaitAsync(Limite);

        cts.IsCancellationRequested.Should().BeFalse("o stream fechou sozinho, não por cancelamento");
    }

    [Fact]
    public async Task FechaQuandoOCarimboRevoga()
    {
        var relogio = new FakeTimeProvider(Inicio);
        using var ouvinte = _broker.SubscribeOperacao("carimbo", _empresaId);
        var (resposta, _) = Resposta();
        using var cts = new CancellationTokenSource(Limite);
        var conferencias = 0;
        var revogado = 0;

        var stream = TransmissaoSse.TransmitirAsync(
            resposta, ouvinte.Slot, Intervalo, Inicio.AddHours(8),
            _ =>
            {
                Interlocked.Increment(ref conferencias);
                return Task.FromResult(Volatile.Read(ref revogado) == 0);
            },
            relogio, cts.Token);

        await AvancarAte(relogio, () => Volatile.Read(ref conferencias) >= 2);
        stream.IsCompleted.Should().BeFalse("enquanto o carimbo não revoga, o stream continua");

        Volatile.Write(ref revogado, 1);
        await AvancarAte(relogio, () => stream.IsCompleted);
        await stream.WaitAsync(Limite);

        cts.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public async Task HeartbeatContinuaSemRevogacao()
    {
        using var ouvinte = _broker.SubscribeOperacao("heartbeat", _empresaId);
        var (resposta, corpo) = Resposta();
        using var cts = new CancellationTokenSource(Limite);
        var conferencias = 0;

        var stream = TransmissaoSse.TransmitirAsync(
            resposta, ouvinte.Slot, Intervalo, DateTimeOffset.UtcNow.AddHours(1),
            _ =>
            {
                Interlocked.Increment(ref conferencias);
                return Task.FromResult(true);
            },
            TimeProvider.System, cts.Token);

        await Aguardar(() => Ocorrencias(corpo.Texto(), ": heartbeat\n\n") >= 2 && Volatile.Read(ref conferencias) >= 2);
        stream.IsCompleted.Should().BeFalse("sem revogação nem exp, o stream segue aberto");
        await cts.CancelAsync();
        await stream;

        corpo.Texto().Should().StartWith("event: ready\n");
    }

    [Fact]
    public async Task StreamOcupadoTambemConfereOCarimbo()
    {
        // O heartbeat só sai quando a fila fica parada; com pedido entrando o tempo todo, a conferência não
        // pode depender dele, senão uma sessão revogada receberia eventos para sempre.
        //
        // O relógio é falso de propósito: com o relógio de parede e intervalo de 200 ms, uma parada de 200 ms do
        // publicador (runner de CI sob carga) virava heartbeat e o teste falhava sem defeito nenhum no laço. Agora o
        // tempo só anda quando o teste manda, e um heartbeat exigiria o relógio falso passar do intervalo sem evento.
        var relogio = new FakeTimeProvider(Inicio);
        using var ouvinte = _broker.SubscribeOperacao("ocupado", _empresaId);
        var (resposta, corpo) = Resposta();
        using var cts = new CancellationTokenSource(Limite);
        var conferencias = 0;
        var intervalo = TimeSpan.FromSeconds(30);
        var passo = TimeSpan.FromSeconds(11); // menor que o intervalo: a fila nunca fica parada por ele inteiro

        var stream = TransmissaoSse.TransmitirAsync(
            resposta, ouvinte.Slot, intervalo, Inicio.AddHours(1),
            _ => Task.FromResult(Interlocked.Increment(ref conferencias) < 3),
            relogio, cts.Token);

        for (var n = 0; !stream.IsCompleted; n++)
        {
            if (n > 500) throw new TimeoutException("o stream não fechou na 3ª conferência");
            _broker.PublicarOperacao(_empresaId, "pedido.pago", new { n });
            relogio.Advance(passo);
            await Task.Delay(5);
        }

        await stream.WaitAsync(Limite);

        Volatile.Read(ref conferencias).Should().Be(3, "fechou na 3ª conferência");
        corpo.Texto().Should().Contain("event: pedido.pago").And.NotContain(": heartbeat",
            "a fila nunca ficou parada pelo intervalo, e mesmo assim o carimbo foi conferido");
    }

    [Fact]
    public async Task SemExpENemVerificadorSoSaiPorCancelamento()
    {
        using var ouvinte = _broker.SubscribeOperacao("legado", _empresaId);
        var (resposta, corpo) = Resposta();
        using var cts = new CancellationTokenSource(Limite);

        var stream = TransmissaoSse.TransmitirAsync(resposta, ouvinte.Slot, Intervalo, null, null, TimeProvider.System, cts.Token);
        await Aguardar(() => Ocorrencias(corpo.Texto(), ": heartbeat\n\n") >= 2);
        stream.IsCompleted.Should().BeFalse();

        await cts.CancelAsync();
        await stream;
    }

    // ── infraestrutura do teste ───────────────────────────────────────────────────────────────

    private static (HttpResponse Resposta, CorpoObservavel Corpo) Resposta()
    {
        var contexto = new DefaultHttpContext();
        var corpo = new CorpoObservavel();
        contexto.Response.Body = corpo;
        return (contexto.Response, corpo);
    }

    private static async Task Aguardar(Func<bool> condicao)
    {
        var fim = DateTime.UtcNow + Limite;
        while (!condicao())
        {
            if (DateTime.UtcNow > fim) throw new TimeoutException("condição não atingida");
            await Task.Delay(10);
        }
    }

    /// <summary>Anda o relógio falso aos poucos até a condição valer: o laço mede o tempo por ele.</summary>
    private static async Task AvancarAte(FakeTimeProvider relogio, Func<bool> condicao)
    {
        var fim = DateTime.UtcNow + Limite;
        while (!condicao())
        {
            if (DateTime.UtcNow > fim) throw new TimeoutException("condição não atingida");
            relogio.Advance(Intervalo + TimeSpan.FromMilliseconds(1));
            await Task.Delay(10);
        }
    }

    private static int Ocorrencias(string texto, string trecho)
    {
        var n = 0;
        for (var i = texto.IndexOf(trecho, StringComparison.Ordinal); i >= 0; i = texto.IndexOf(trecho, i + 1, StringComparison.Ordinal)) n++;
        return n;
    }

    /// <summary>Corpo da resposta legível enquanto o stream ainda escreve.</summary>
    private sealed class CorpoObservavel : MemoryStream
    {
        private readonly object _trava = new();

        public override void Write(byte[] buffer, int offset, int count)
        {
            lock (_trava) base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            lock (_trava) base.Write(buffer);
        }

        public string Texto()
        {
            lock (_trava) return Encoding.UTF8.GetString(ToArray());
        }
    }
}

using EasyStock.Application.Reporting;
using EasyStock.Domain.Reporting;
using EasyStock.Infra.Async.Reporting.Exporters;
using FluentAssertions;

namespace EasyStock.Infra.Async.UnitTests.Reporting.Exporters;

/// <summary>
/// Probe de #364: prova empiricamente EM QUE thread a serialização bloqueante do
/// MiniExcel (Read() sync-over-async) de fato roda.
///
/// O export é disparado via <c>Task.Run</c> para garantir que o chamador inicial é
/// uma thread do ThreadPool (remove a ambiguidade da thread do test-runner). O
/// enumerator captura <c>IsThreadPoolThread</c> na parte síncrona do MoveNextAsync —
/// exatamente a thread em que o MiniExcel chama Read().
///
/// - ANTES do fix (await MiniExcel.SaveAsAsync, pull no pool) → captura thread do pool → VERMELHO.
/// - DEPOIS (Task.Factory.StartNew LongRunning) → captura thread dedicada → VERDE.
/// - Se VERDE antes do fix → revela que o MiniExcel já offloada (Caso 2) → fix é no-op (critério de parada).
///
/// Determinístico (booleano, sem timing) → não é flaky.
/// </summary>
public class ExcelExporterThreadPoolProbeTests
{
    private sealed class Linha
    {
        public string Col { get; init; } = "x";
    }

    private sealed class ThreadCapturingRows : IAsyncEnumerable<Linha>
    {
        private readonly int _count;
        public bool? ReadThreadIsPool { get; private set; }

        public ThreadCapturingRows(int count) => _count = count;

        public IAsyncEnumerator<Linha> GetAsyncEnumerator(CancellationToken ct = default)
            => new Enumerator(this, _count);

        private sealed class Enumerator(ThreadCapturingRows owner, int count) : IAsyncEnumerator<Linha>
        {
            private int _i;
            public Linha Current { get; private set; } = new();

            public ValueTask<bool> MoveNextAsync()
            {
                // Corpo 100% síncrono → roda na thread que chamou Read() (a da serialização).
                owner.ReadThreadIsPool ??= Thread.CurrentThread.IsThreadPoolThread;
                if (_i >= count) return ValueTask.FromResult(false);
                _i++;
                Current = new Linha();
                return ValueTask.FromResult(true);
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private static ReportSchema Schema() => new(
        title: "Probe",
        fileNameBase: "probe",
        columns: [new("Col", "Col", 0)]);

    [Trait("Category", "Threading")]
    [Fact]
    public async Task WriteAsync_SerializaFora_DoThreadPoolCompartilhado()
    {
        var rows = new ThreadCapturingRows(count: 50);
        using var output = new MemoryStream();

        // Task.Run: chamador inicial garantidamente é thread do pool.
        await Task.Run(() => new ExcelExporter().WriteAsync(
            rows, Schema(), output, new ReportExportOptions(), CancellationToken.None));

        rows.ReadThreadIsPool.Should().NotBeNull("Read() precisa ter sido invocado pelo MiniExcel");
        rows.ReadThreadIsPool!.Value.Should().BeFalse(
            "a serialização bloqueante (Read sync-over-async) deve rodar numa thread DEDICADA (LongRunning), " +
            "não numa thread do ThreadPool compartilhado — senão N exports concorrentes faminta o pool que " +
            "serve os outros relatórios e hosted services do Worker (#364).");
    }
}

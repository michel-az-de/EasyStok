using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Infra.Async.UnitTests.Caching;

/// <summary>
/// #282: concorrência do IncrementAsync. Garante que o lock por-chave (agora com
/// timeout defensivo, não <c>Wait()</c> indefinido) serializa corretamente o
/// read-modify-write — N incrementos concorrentes na mesma chave produzem o total
/// exato, sem deadlock nem perda de atualização.
/// </summary>
public class InMemoryCacheServiceIncrementTests
{
    private static InMemoryCacheService NewService() =>
        new(new MemoryCache(new MemoryCacheOptions()), NullLogger<InMemoryCacheService>.Instance);

    [Fact]
    public async Task IncrementAsync_100ThreadsNaMesmaChave_TotalExato_SemDeadlock()
    {
        var cache = NewService();
        const int n = 100;

        var tasks = Enumerable.Range(0, n)
            .Select(_ => Task.Run(() => cache.IncrementAsync("contador", 1)));

        // Se houvesse deadlock, o WhenAll nunca completaria; o timeout do teste falharia.
        await Task.WhenAll(tasks);

        var total = await cache.IncrementAsync("contador", 0); // lê sem alterar
        total.Should().Be(n, "100 incrementos concorrentes de +1 devem somar exatamente 100");
    }

    [Fact]
    public async Task IncrementAsync_ValoresVariados_Concorrente_SomaConsistente()
    {
        var cache = NewService();
        var incrementos = new[] { 1L, 2L, 3L, 4L, 5L, 10L, 20L, 50L };
        var esperado = incrementos.Sum();

        await Task.WhenAll(incrementos.Select(v => Task.Run(() => cache.IncrementAsync("soma", v))));

        var total = await cache.IncrementAsync("soma", 0);
        total.Should().Be(esperado);
    }
}

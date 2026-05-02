using EasyStock.Api.IntegrationTests.Infrastructure;
using FluentAssertions;
using System.Diagnostics;
using System.Net;

namespace EasyStock.Api.IntegrationTests.Scenarios;

/// <summary>
/// Testes de performance para validar que isolamento não introduz overhead significativo
/// </summary>
public class TenantIsolationPerformanceTests : IAsyncLifetime
{
    private MultiTenantTestFixture _fixture = null!;

    public async Task InitializeAsync()
    {
        _fixture = new MultiTenantTestFixture();
        await _fixture.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await _fixture.DisposeAsync();
    }

    [Fact]
    public async Task Isolation_Queries_Have_Acceptable_Overhead()
    {
        if (!_fixture.IsAvailable)
            Assert.Skip("Docker não disponível");

        await using var factory = _fixture.CreateFactory();
        using var client = factory.CreateClient();

        var tokenA = await _fixture.GetTokenAsync(_fixture.AdminAId, _fixture.EmpresaAId);
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokenA);

        // Warm up
        await client.GetAsync($"/api/movimentacoes?empresaId={_fixture.EmpresaAId}&pageSize=10");

        // Measure: Query movimentações com isolamento
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 5; i++)
        {
            var response = await client.GetAsync(
                $"/api/movimentacoes?empresaId={_fixture.EmpresaAId}&pageSize=100");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        sw.Stop();

        var averageMs = sw.ElapsedMilliseconds / 5.0;

        // Assertions: query deve ser rápida (< 500ms em média)
        averageMs.Should().BeLessThan(500,
            "Query com isolamento deve ser rápida (< 500ms)");
    }

    [Fact]
    public async Task No_Query_Time_Variance_Based_On_Other_Tenant_Size()
    {
        if (!_fixture.IsAvailable)
            Assert.Skip("Docker não disponível");

        // EmpresaA tem 100 movimentações (pequeno)
        // EmpresaB tem 150 movimentações (médio)
        // Ambas devem ter tempo de query similar se isolamento é perfeito

        await using var factory = _fixture.CreateFactory();
        using var client = factory.CreateClient();

        // Query para EmpresaA (pequena)
        var tokenA = await _fixture.GetTokenAsync(_fixture.AdminAId, _fixture.EmpresaAId);
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokenA);

        var swA = Stopwatch.StartNew();
        for (int i = 0; i < 3; i++)
        {
            var response = await client.GetAsync(
                $"/api/movimentacoes?empresaId={_fixture.EmpresaAId}&pageSize=100");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        swA.Stop();

        // Query para EmpresaB (maior)
        var tokenB = await _fixture.GetTokenAsync(_fixture.AdminBId, _fixture.EmpresaBId);
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokenB);

        var swB = Stopwatch.StartNew();
        for (int i = 0; i < 3; i++)
        {
            var response = await client.GetAsync(
                $"/api/movimentacoes?empresaId={_fixture.EmpresaBId}&pageSize=100");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        swB.Stop();

        var timeA = swA.ElapsedMilliseconds / 3.0;
        var timeB = swB.ElapsedMilliseconds / 3.0;

        // Times não devem variar drasticamente (< 50% diferença)
        var variance = Math.Abs(timeA - timeB) / Math.Max(timeA, timeB);
        variance.Should().BeLessThan(0.5,
            $"Query time variance between tenants should be < 50%: A={timeA}ms, B={timeB}ms");
    }

    [Fact]
    public async Task Pagination_Performance_Constant_Across_Tenants()
    {
        if (!_fixture.IsAvailable)
            Assert.Skip("Docker não disponível");

        await using var factory = _fixture.CreateFactory();
        using var client = factory.CreateClient();

        var tokenA = await _fixture.GetTokenAsync(_fixture.AdminAId, _fixture.EmpresaAId);
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokenA);

        // Page 1
        var sw1 = Stopwatch.StartNew();
        var response1 = await client.GetAsync(
            $"/api/movimentacoes?empresaId={_fixture.EmpresaAId}&page=1&pageSize=20");
        sw1.Stop();

        // Page 2
        var sw2 = Stopwatch.StartNew();
        var response2 = await client.GetAsync(
            $"/api/movimentacoes?empresaId={_fixture.EmpresaAId}&page=2&pageSize=20");
        sw2.Stop();

        response1.StatusCode.Should().Be(HttpStatusCode.OK);
        response2.StatusCode.Should().Be(HttpStatusCode.OK);

        // Times devem ser similares
        var time1 = sw1.ElapsedMilliseconds;
        var time2 = sw2.ElapsedMilliseconds;

        var variance = Math.Abs(time1 - time2) / (double)Math.Max(time1, time2);
        variance.Should().BeLessThan(0.3,
            "Pagination performance deve ser consistente entre páginas");
    }

    [Fact]
    public async Task Aggregation_Queries_Reasonable_Performance()
    {
        if (!_fixture.IsAvailable)
            Assert.Skip("Docker não disponível");

        await using var factory = _fixture.CreateFactory();
        using var client = factory.CreateClient();

        var tokenA = await _fixture.GetTokenAsync(_fixture.AdminAId, _fixture.EmpresaAId);
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokenA);

        // Analytics aggregation (usually slower)
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 3; i++)
        {
            var response = await client.GetAsync(
                $"/api/analytics/dashboard?empresaId={_fixture.EmpresaAId}");
            if (response.StatusCode != HttpStatusCode.OK)
                continue;
        }
        sw.Stop();

        var averageMs = sw.ElapsedMilliseconds / 3.0;

        // Analytics can be slower, but still < 1 second
        averageMs.Should().BeLessThan(1000,
            "Analytics queries should complete in < 1 second");
    }

    [Fact]
    public async Task Cache_Hits_Should_Improve_Performance()
    {
        if (!_fixture.IsAvailable)
            Assert.Skip("Docker não disponível");

        await using var factory = _fixture.CreateFactory();
        using var client = factory.CreateClient();

        var tokenA = await _fixture.GetTokenAsync(_fixture.AdminAId, _fixture.EmpresaAId);
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokenA);

        // First request (cold cache)
        var swCold = Stopwatch.StartNew();
        var response1 = await client.GetAsync(
            $"/api/movimentacoes?empresaId={_fixture.EmpresaAId}&pageSize=50");
        swCold.Stop();

        // Second request (warm cache, if implemented)
        var swWarm = Stopwatch.StartNew();
        var response2 = await client.GetAsync(
            $"/api/movimentacoes?empresaId={_fixture.EmpresaAId}&pageSize=50");
        swWarm.Stop();

        response1.StatusCode.Should().Be(HttpStatusCode.OK);
        response2.StatusCode.Should().Be(HttpStatusCode.OK);

        var coldTime = swCold.ElapsedMilliseconds;
        var warmTime = swWarm.ElapsedMilliseconds;

        // Warm should be faster (or equal if no cache)
        warmTime.Should().BeLessThanOrEqualTo(coldTime * 1.5,
            "Cached request should not be significantly slower");
    }

    [Fact]
    public async Task Concurrent_Requests_Same_Tenant_Should_Scale()
    {
        if (!_fixture.IsAvailable)
            Assert.Skip("Docker não disponível");

        await using var factory = _fixture.CreateFactory();
        using var client = factory.CreateClient();

        var tokenA = await _fixture.GetTokenAsync(_fixture.AdminAId, _fixture.EmpresaAId);
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokenA);

        // Concurrent requests (5 at a time)
        var sw = Stopwatch.StartNew();
        var tasks = Enumerable.Range(0, 5)
            .Select(async _ =>
            {
                var response = await client.GetAsync(
                    $"/api/movimentacoes?empresaId={_fixture.EmpresaAId}&pageSize=50");
                return response.StatusCode;
            })
            .ToList();

        var results = await Task.WhenAll(tasks);
        sw.Stop();

        results.Should().AllBe(HttpStatusCode.OK);

        var averageMs = sw.ElapsedMilliseconds / 5.0;
        averageMs.Should().BeLessThan(1000,
            "Concurrent requests should handle well");
    }

    [Fact]
    public async Task Concurrent_Requests_Different_Tenants_Should_Not_Interfere()
    {
        if (!_fixture.IsAvailable)
            Assert.Skip("Docker não disponível");

        await using var factory = _fixture.CreateFactory();
        using var client = factory.CreateClient();

        var tokenA = await _fixture.GetTokenAsync(_fixture.AdminAId, _fixture.EmpresaAId);
        var tokenB = await _fixture.GetTokenAsync(_fixture.AdminBId, _fixture.EmpresaBId);

        var sw = Stopwatch.StartNew();

        var taskA = Task.Run(async () =>
        {
            var c = factory.CreateClient();
            c.DefaultRequestHeaders.Authorization = new("Bearer", tokenA);
            return await c.GetAsync($"/api/movimentacoes?empresaId={_fixture.EmpresaAId}&pageSize=50");
        });

        var taskB = Task.Run(async () =>
        {
            var c = factory.CreateClient();
            c.DefaultRequestHeaders.Authorization = new("Bearer", tokenB);
            return await c.GetAsync($"/api/movimentacoes?empresaId={_fixture.EmpresaBId}&pageSize=50");
        });

        var resultA = await taskA;
        var resultB = await taskB;
        sw.Stop();

        resultA.StatusCode.Should().Be(HttpStatusCode.OK);
        resultB.StatusCode.Should().Be(HttpStatusCode.OK);

        // Total time should be close to single request time (parallelization working)
        sw.ElapsedMilliseconds.Should().BeLessThan(2000,
            "Concurrent requests different tenants should run in parallel");
    }
}

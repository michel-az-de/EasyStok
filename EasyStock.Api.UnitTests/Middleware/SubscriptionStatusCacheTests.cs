using EasyStock.Application.Ports.Output.Caching;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Caching;
using EasyStock.Infra.Postgre.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace EasyStock.Api.UnitTests.Middleware;

public class SubscriptionStatusCacheTests
{
    private static SubscriptionStatusCache Build(TimeSpan? ttl = null)
    {
        var memCache = new MemoryCache(new MemoryCacheOptions());
        var opts = Options.Create(new CacheOptions
        {
            SubscriptionStatusDuration = ttl ?? TimeSpan.FromMinutes(1)
        });
        return new SubscriptionStatusCache(memCache, opts);
    }

    [Fact]
    public async Task Miss_chama_fetch_e_armazena()
    {
        var cache = Build();
        var empresa = Guid.NewGuid();
        var snapshot = new SubscriptionStatusSnapshot(StatusAssinatura.Ativa, null, null);
        var calls = 0;

        var first = await cache.GetOrFetchAsync(empresa, _ => { calls++; return Task.FromResult<SubscriptionStatusSnapshot?>(snapshot); });
        var second = await cache.GetOrFetchAsync(empresa, _ => { calls++; return Task.FromResult<SubscriptionStatusSnapshot?>(snapshot); });

        first.Should().Be(snapshot);
        second.Should().Be(snapshot);
        calls.Should().Be(1);
    }

    [Fact]
    public async Task GuidEmpty_bypassa_cache_e_sempre_fetch()
    {
        var cache = Build();
        var snapshot = new SubscriptionStatusSnapshot(StatusAssinatura.Ativa, null, null);
        var calls = 0;

        await cache.GetOrFetchAsync(Guid.Empty, _ => { calls++; return Task.FromResult<SubscriptionStatusSnapshot?>(snapshot); });
        await cache.GetOrFetchAsync(Guid.Empty, _ => { calls++; return Task.FromResult<SubscriptionStatusSnapshot?>(snapshot); });

        calls.Should().Be(2);
    }

    [Fact]
    public async Task Cacheia_ausencia_e_retorna_null_no_hit()
    {
        var cache = Build();
        var empresa = Guid.NewGuid();
        var calls = 0;

        var first = await cache.GetOrFetchAsync(empresa, _ => { calls++; return Task.FromResult<SubscriptionStatusSnapshot?>(null); });
        var second = await cache.GetOrFetchAsync(empresa, _ => { calls++; return Task.FromResult<SubscriptionStatusSnapshot?>(null); });

        first.Should().BeNull();
        second.Should().BeNull();
        calls.Should().Be(1);
    }

    [Fact]
    public async Task Invalidate_forca_proximo_fetch()
    {
        var cache = Build();
        var empresa = Guid.NewGuid();
        var ativa = new SubscriptionStatusSnapshot(StatusAssinatura.Ativa, null, null);
        var suspensa = new SubscriptionStatusSnapshot(StatusAssinatura.Suspensa, null, null);
        var calls = 0;
        SubscriptionStatusSnapshot? proximo = ativa;

        var a = await cache.GetOrFetchAsync(empresa, _ => { calls++; return Task.FromResult<SubscriptionStatusSnapshot?>(proximo); });
        cache.Invalidate(empresa);
        proximo = suspensa;
        var b = await cache.GetOrFetchAsync(empresa, _ => { calls++; return Task.FromResult<SubscriptionStatusSnapshot?>(proximo); });

        a.Should().Be(ativa);
        b.Should().Be(suspensa);
        calls.Should().Be(2);
    }

    [Fact]
    public async Task Invalidate_em_GuidEmpty_nao_quebra()
    {
        var cache = Build();
        var act = () => { cache.Invalidate(Guid.Empty); return Task.CompletedTask; };
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Tenants_diferentes_tem_buckets_isolados()
    {
        var cache = Build();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var calls = 0;

        await cache.GetOrFetchAsync(a, _ => { calls++; return Task.FromResult<SubscriptionStatusSnapshot?>(new(StatusAssinatura.Ativa, null, null)); });
        await cache.GetOrFetchAsync(b, _ => { calls++; return Task.FromResult<SubscriptionStatusSnapshot?>(new(StatusAssinatura.Suspensa, null, null)); });
        await cache.GetOrFetchAsync(a, _ => { calls++; return Task.FromResult<SubscriptionStatusSnapshot?>(new(StatusAssinatura.Ativa, null, null)); });

        calls.Should().Be(2);
    }

    [Fact]
    public async Task Ttl_zero_aplica_default_60s()
    {
        // CacheOptions com Duration default zero (mal configurado) ainda cacheia (nao explode).
        var cache = new SubscriptionStatusCache(
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new CacheOptions { SubscriptionStatusDuration = TimeSpan.Zero }));
        var empresa = Guid.NewGuid();
        var calls = 0;

        await cache.GetOrFetchAsync(empresa, _ => { calls++; return Task.FromResult<SubscriptionStatusSnapshot?>(new(StatusAssinatura.Ativa, null, null)); });
        await cache.GetOrFetchAsync(empresa, _ => { calls++; return Task.FromResult<SubscriptionStatusSnapshot?>(new(StatusAssinatura.Ativa, null, null)); });

        calls.Should().Be(1);
    }
}

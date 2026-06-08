using EasyStock.Application.Ports.Output;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.UseCases;

public class ProdutoCacheInvalidatorTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Produto = Guid.NewGuid();

    [Fact]
    public async Task InvalidarSaldoAsync_remove_o_cache_de_produto_detalhe()
    {
        var cache = new FakeCacheService();
        await cache.SetAsync(CacheKeys.Produto(Empresa, Produto), 56);
        var sut = new ProdutoCacheInvalidator(cache, NullLogger<ProdutoCacheInvalidator>.Instance);

        await sut.InvalidarSaldoAsync(Empresa, new[] { Produto });

        // produto:{e}:{p} esta dentro de ProdutoRelacionadas -> deve sumir.
        (await cache.ExistsAsync(CacheKeys.Produto(Empresa, Produto))).Should().BeFalse();
    }

    [Fact]
    public async Task InvalidarSaldoAsync_nao_afeta_outra_empresa()
    {
        var outraEmpresa = Guid.NewGuid();
        var cache = new FakeCacheService();
        await cache.SetAsync(CacheKeys.Produto(Empresa, Produto), 56);
        await cache.SetAsync(CacheKeys.Produto(outraEmpresa, Produto), 99);
        var sut = new ProdutoCacheInvalidator(cache, NullLogger<ProdutoCacheInvalidator>.Instance);

        await sut.InvalidarSaldoAsync(Empresa, new[] { Produto });

        (await cache.ExistsAsync(CacheKeys.Produto(outraEmpresa, Produto))).Should().BeTrue();
    }

    [Fact]
    public async Task InvalidarSaldoAsync_e_best_effort_nao_relanca_quando_o_cache_falha()
    {
        // G2: a operacao ja persistiu; lancar aqui faria o usuario re-registrar e dobrar o saldo.
        var cache = new FakeCacheService { ThrowOnRemove = true };
        var sut = new ProdutoCacheInvalidator(cache, NullLogger<ProdutoCacheInvalidator>.Instance);

        var act = async () => await sut.InvalidarSaldoAsync(Empresa, new[] { Produto });

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task InvalidarSaldoAsync_deduplica_produtos()
    {
        var cache = new FakeCacheService();
        var sut = new ProdutoCacheInvalidator(cache, NullLogger<ProdutoCacheInvalidator>.Instance);
        var outroProduto = Guid.NewGuid();

        await sut.InvalidarSaldoAsync(Empresa, new[] { Produto, Produto, outroProduto });

        cache.RemoveCalls.Should().Be(2, "produtos distintos: 2 chamadas de RemoveAsync, nao 3");
    }

    /// <summary>Fake dict-backed de ICacheService: Set/Get/Remove/Exists reais; o resto nao e usado.</summary>
    private sealed class FakeCacheService : ICacheService
    {
        private readonly Dictionary<string, object?> _store = new();
        public bool ThrowOnRemove { get; init; }
        public int RemoveCalls { get; private set; }

        public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null)
        {
            _store[key] = value;
            return Task.CompletedTask;
        }

        public Task<T?> GetAsync<T>(string key)
        {
            if (_store.TryGetValue(key, out var v) && v is T t) return Task.FromResult<T?>(t);
            return Task.FromResult<T?>(default);
        }

        public Task RemoveAsync(string key) => Remove(new[] { key });
        public Task RemoveAsync(IEnumerable<string> keys) => Remove(keys);
        public Task<bool> ExistsAsync(string key) => Task.FromResult(_store.ContainsKey(key));
        public Task<long> IncrementAsync(string key, long value = 1) => throw new NotSupportedException();
        public Task SetExpiryAsync(string key, TimeSpan ttl) => Task.CompletedTask;

        private Task Remove(IEnumerable<string> keys)
        {
            RemoveCalls++;
            if (ThrowOnRemove) throw new InvalidOperationException("cache indisponivel");
            foreach (var k in keys) _store.Remove(k);
            return Task.CompletedTask;
        }
    }
}

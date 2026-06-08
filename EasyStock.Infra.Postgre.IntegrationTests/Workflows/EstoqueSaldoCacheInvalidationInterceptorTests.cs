using EasyStock.Application.Ports.Output;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.DependencyInjection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Workflows;

/// <summary>
/// Prova que o <c>EstoqueSaldoCacheInvalidationInterceptor</c> esta wired pela
/// registracao de PRODUCAO (<c>AddEasyStockPostgreInfrastructure</c> -> <c>AddInterceptors</c>)
/// e invalida <c>produto:{e}:{p}</c> ao salvar um <c>ItemEstoque</c> — fechando o BUG-009 (#517).
/// Inclui o controle NEGATIVO (red-green): o mesmo cenario sem o interceptor (DbContext bare)
/// NAO invalida. O harness usa a DI real, nao um DbContext montado a mao (Socratic #1).
/// </summary>
public class EstoqueSaldoCacheInvalidationInterceptorTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task Mutacao_de_ItemEstoque_pelo_DbContext_de_producao_invalida_o_cache_de_saldo()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();

        var empresaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        await SeedAsync(empresaId, produtoId, quantidade: 56);

        var cache = new SpyCacheService();
        await cache.SetAsync(CacheKeys.Produto(empresaId, produtoId), 56); // tela leu o saldo antigo

        await using var provider = BuildProductionProvider(cache);
        await using (var scope = provider.CreateAsyncScope())
        {
            // DbContext de PRODUCAO: o interceptor foi plugado pelo .AddInterceptors do
            // AddEasyStockPostgreInfrastructure (prova o wiring). Mutar a entidade rastreada
            // via _db = o mesmo caminho do #6 MobileStockReconciler.
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(empresaId);
            var item = await db.ItensEstoque.SingleAsync(i => i.ProdutoId == produtoId);
            item.RestaurarQuantidade(Quantidade.From(10), DateTime.UtcNow); // 56 -> 66
            await db.SaveChangesAsync();
        }

        cache.RemovedKeys.Should().Contain(CacheKeys.Produto(empresaId, produtoId),
            "o interceptor registrado em producao deve invalidar produto:{e}:{p} ao salvar ItemEstoque");
        (await cache.ExistsAsync(CacheKeys.Produto(empresaId, produtoId))).Should().BeFalse();
    }

    [SkippableFact]
    public async Task Sem_o_interceptor_DbContext_bare_o_cache_de_saldo_fica_stale()
    {
        // Controle NEGATIVO (red-green): sem o interceptor, a mesma mutacao NAO invalida.
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();

        var empresaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        await SeedAsync(empresaId, produtoId, quantidade: 56);

        var cache = new SpyCacheService();
        await cache.SetAsync(CacheKeys.Produto(empresaId, produtoId), 56);

        // fixture.CreateDbContext() NAO pluga interceptors -> nada toca o cache.
        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresaId);
            var item = await db.ItensEstoque.SingleAsync(i => i.ProdutoId == produtoId);
            item.RestaurarQuantidade(Quantidade.From(10), DateTime.UtcNow);
            await db.SaveChangesAsync();
        }

        cache.RemovedKeys.Should().BeEmpty();
        (await cache.ExistsAsync(CacheKeys.Produto(empresaId, produtoId)))
            .Should().BeTrue("sem o interceptor o saldo fica stale ate o TTL");
    }

    [SkippableFact]
    public async Task Salvar_outra_entidade_nao_invalida_o_cache_de_saldo()
    {
        // Negativo por tipo (com o filtro amplo): a Capture filtra Entries<ItemEstoque>(),
        // entao salvar um Produto no mesmo DbContext nao dispara invalidacao de saldo.
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();

        var empresaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        await SeedAsync(empresaId, produtoId, quantidade: 56);

        var cache = new SpyCacheService();
        await using var provider = BuildProductionProvider(cache);
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(empresaId);
            var produto = await db.Set<Produto>().SingleAsync(p => p.Id == produtoId);
            produto.Nome = "Nome novo"; // muta Produto, nao ItemEstoque
            await db.SaveChangesAsync();
        }

        cache.RemovedKeys.Should().BeEmpty();
    }

    [SkippableFact]
    public async Task SaveChanges_SINCRONO_tambem_invalida_branch_defensivo()
    {
        // SINTETICO (defensivo): nenhum mutador de producao salva sincrono hoje, mas o
        // interceptor cobre os DOIS caminhos. Este caso exercita o branch sync (SavedChanges +
        // GetAwaiter().GetResult()) explicitamente, senao so a metade async ficaria coberta.
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();

        var empresaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        await SeedAsync(empresaId, produtoId, quantidade: 56);

        var cache = new SpyCacheService();
        await using var provider = BuildProductionProvider(cache);
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(empresaId);
            var item = db.ItensEstoque.Single(i => i.ProdutoId == produtoId);
            item.RestaurarQuantidade(Quantidade.From(10), DateTime.UtcNow);
            db.SaveChanges(); // SINCRONO -> exercita SavedChanges (sync)
        }

        cache.RemovedKeys.Should().Contain(CacheKeys.Produto(empresaId, produtoId));
    }

    private ServiceProvider BuildProductionProvider(ICacheService cache)
    {
        var config = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddSingleton(Substitute.For<ICurrentUserAccessor>());
        services.AddSingleton<ICacheService>(cache); // consumido pelo ProdutoCacheInvalidator
        services.AddEasyStockPostgreInfrastructure(fixture.ConnectionString, config);
        return services.BuildServiceProvider();
    }

    private async Task SeedAsync(Guid empresaId, Guid produtoId, int quantidade)
    {
        var categoriaId = Guid.NewGuid();
        await using var ctx = fixture.CreateDbContext();
        ctx.Set<Empresa>().Add(new Empresa
        {
            Id = empresaId,
            Nome = "Empresa Teste",
            Documento = $"{Random.Shared.Next(100000, 999999)}",
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        });
        ctx.Set<Categoria>().Add(new Categoria
        {
            Id = categoriaId,
            EmpresaId = empresaId,
            Nome = "Audio",
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        });
        ctx.Set<Produto>().Add(new Produto
        {
            Id = produtoId,
            EmpresaId = empresaId,
            CategoriaId = categoriaId,
            Nome = "Galaxy Buds FE",
            Tipo = TipoProduto.Fisico,
            Status = StatusProduto.Ativo,
            PrecoReferencia = Dinheiro.FromDecimal(399.90m),
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        });
        ctx.Set<ItemEstoque>().Add(new ItemEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = produtoId,
            QuantidadeInicial = Quantidade.From(quantidade),
            QuantidadeAtual = Quantidade.From(quantidade),
            CustoUnitario = Dinheiro.FromDecimal(200m),
            Status = StatusItemEstoque.Ok,
            EntradaEm = DateTime.UtcNow,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
    }

    /// <summary>Spy de ICacheService: registra as chaves removidas e responde Exists/Get/Set.</summary>
    private sealed class SpyCacheService : ICacheService
    {
        private readonly Dictionary<string, object?> _store = new();
        public List<string> RemovedKeys { get; } = new();

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

        public Task RemoveAsync(string key)
        {
            RemovedKeys.Add(key);
            _store.Remove(key);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(IEnumerable<string> keys)
        {
            foreach (var k in keys)
            {
                RemovedKeys.Add(k);
                _store.Remove(k);
            }
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string key) => Task.FromResult(_store.ContainsKey(key));
        public Task<long> IncrementAsync(string key, long value = 1) => throw new NotSupportedException();
        public Task SetExpiryAsync(string key, TimeSpan ttl) => Task.CompletedTask;
    }
}

using EasyStock.Api.Mobile.Services;
using EasyStock.Api.Mobile.Services.Linkers;
using EasyStock.Application.DependencyInjection;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.UseCases.RegistrarEntradaEstoque;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.DependencyInjection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Workflows;

/// <summary>
/// #1526: o lote que o PWA produz chega pelo sync (BatchLinker) e baixa insumo e embalagem com a
/// mesma regra da produção do console. Receita por porção: 300 g de molho e 1 bandeja.
/// </summary>
public class LotePwaBaixaInsumoIntegrationTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task LoteDoPwa_BaixaEmbalagemSempre_EAReceitaSoNoPratoMarcado_UmaVezSo()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();
        var s = await SeedAsync();
        await using var provider = BuildProductionProvider();

        await Escopo(provider, s.EmpresaId, async sp =>
        {
            var entrada = sp.GetRequiredService<RegistrarEntradaEstoqueUseCase>();
            foreach (var (id, qtd) in new[] { (s.MolhoId, 1000m), (s.BandejaId, 20m) })
                await entrada.ExecuteAsync(new RegistrarEntradaEstoqueCommand(s.EmpresaId, id, null, qtd, 0.1m, null, DateTime.UtcNow.AddDays(-1),
                    NaturezaMovimentacaoEstoque.Compra, null, null, null, null, null, null, null, null, null, null, null, null, null));
        });

        // 1) Prato sem a marca: 3 porções → só 3 bandejas (D-M2-03); o molho fica.
        await Sincronizar(provider, s, "b-sem-marca", 3);
        (await Saldo(s.EmpresaId, s.BandejaId), await Saldo(s.EmpresaId, s.MolhoId)).Should().Be((17m, 1000m));

        // 2) Prato marcado: 4 porções → 4 bandejas e 1.200 g de molho, mas só há 1.000 g: avisa, não trava.
        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(s.EmpresaId);
            (await db.Set<Produto>().SingleAsync(p => p.Id == s.PratoId)).BaixaInsumoAutomatica = true;
            await db.SaveChangesAsync();
        }
        await Sincronizar(provider, s, "b-marcado", 4);
        (await Saldo(s.EmpresaId, s.BandejaId), await Saldo(s.EmpresaId, s.MolhoId)).Should().Be((13m, 0m));

        // 3) O mesmo lote sincronizado de novo não baixa outra vez.
        await Sincronizar(provider, s, "b-marcado", 4, criar: false);
        (await Saldo(s.EmpresaId, s.BandejaId)).Should().Be(13m);

        await using var assert = fixture.CreateDbContext();
        assert.SetMobileTenantContext(s.EmpresaId);
        var lote = await assert.Set<Lote>().SingleAsync(l => l.MobileBatchId == "b-marcado");
        lote.Observacoes.Should().Contain("Faltou 200 G de Molho");
        (await assert.Set<ItemEstoque>().SingleAsync(i => i.ProdutoId == s.PratoId && i.CodigoInterno!.StartsWith($"lote:{lote.Id}")))
            .QuantidadeAtual.Value.Should().Be(4, "a entrada do lote (F8-A) segue igual");
    }

    private sealed record Semente(Guid EmpresaId, Guid PratoId, Guid MolhoId, Guid BandejaId);

    private async Task Sincronizar(ServiceProvider provider, Semente s, string batchId, int porcoes, bool criar = true)
    {
        if (criar)
        {
            await using var seed = fixture.CreateDbContext();
            seed.SetMobileTenantContext(s.EmpresaId);
            seed.Set<Batch>().Add(new Batch
            {
                Id = batchId, Code = "LOT-PWA", EmpresaId = s.EmpresaId, CreatedAt = DateTime.UtcNow,
                Items = [new BatchItem { BatchId = batchId, ProductId = "p-lasanha", Name = "Lasanha", Qty = porcoes, WeightG = 500 }],
            });
            await seed.SaveChangesAsync();
        }
        await Escopo(provider, s.EmpresaId, async sp =>
        {
            // Resolvido pelo DI como na API: o parâmetro opcional da baixa precisa vir preenchido.
            await sp.GetRequiredService<BatchLinker>().ExecuteAsync([batchId], s.EmpresaId);
        });
    }

    private async Task<decimal> Saldo(Guid empresaId, Guid produtoId)
    {
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresaId);
        return (await db.Set<ItemEstoque>().Where(i => i.ProdutoId == produtoId).ToListAsync()).Sum(i => i.QuantidadeAtual.Value);
    }

    private static async Task Escopo(ServiceProvider provider, Guid empresaId, Func<IServiceProvider, Task> acao)
    {
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<EasyStockDbContext>().SetMobileTenantContext(empresaId);
        await acao(scope.ServiceProvider);
    }

    private async Task<Semente> SeedAsync()
    {
        var s = new Semente(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var categoriaId = Guid.NewGuid();
        var agora = DateTime.UtcNow;
        await using var seed = fixture.CreateDbContext();
        seed.Set<Empresa>().Add(new Empresa
        {
            Id = s.EmpresaId, Nome = "Empresa PWA", Documento = $"{Random.Shared.Next(100000, 999999)}", CriadoEm = agora, AlteradoEm = agora
        });
        seed.Set<Categoria>().Add(new Categoria { Id = categoriaId, EmpresaId = s.EmpresaId, Nome = "Massas", CriadoEm = agora, AlteradoEm = agora });
        Produto Novo(Guid id, string nome, UnidadeMedida unidade, bool insumo, bool embalagem) => new()
        {
            Id = id, EmpresaId = s.EmpresaId, CategoriaId = categoriaId, Nome = nome, Status = StatusProduto.Ativo,
            EhInsumo = insumo, EhEmbalagem = embalagem, UnidadeMedidaBase = unidade, RendimentoBase = 1, RendimentoUnidade = UnidadeMedida.Un,
            CriadoEm = agora, AlteradoEm = agora
        };
        seed.Set<Produto>().AddRange(
            Novo(s.PratoId, "Lasanha", UnidadeMedida.Un, false, false),
            Novo(s.MolhoId, "Molho", UnidadeMedida.G, true, false),
            Novo(s.BandejaId, "Bandeja 800 g", UnidadeMedida.Un, true, true));
        seed.Set<ProdutoComposicao>().AddRange(
            new ProdutoComposicao { Id = Guid.NewGuid(), EmpresaId = s.EmpresaId, ProdutoFinalId = s.PratoId, InsumoId = s.MolhoId,
                Quantidade = 300, Unidade = UnidadeMedida.G, OrdemExibicao = 0, CriadoEm = agora, AlteradoEm = agora },
            new ProdutoComposicao { Id = Guid.NewGuid(), EmpresaId = s.EmpresaId, ProdutoFinalId = s.PratoId, InsumoId = s.BandejaId,
                Quantidade = 1, Unidade = UnidadeMedida.Un, OrdemExibicao = 1, CriadoEm = agora, AlteradoEm = agora });
        seed.Set<Product>().Add(new Product
        {
            Id = "p-lasanha", Name = "Lasanha", Category = "massa", EmpresaId = s.EmpresaId, ErpProductId = s.PratoId
        });
        await seed.SaveChangesAsync();
        return s;
    }

    private ServiceProvider BuildProductionProvider()
    {
        var config = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddSingleton(Substitute.For<ICurrentUserAccessor>());
        services.AddMemoryCache();
        services.AddSingleton(Substitute.For<EasyStock.Application.Ports.Output.ICacheService>());
        services.AddEasyStockPostgreInfrastructure(fixture.ConnectionString, config);
        services.AddEasyStockApplication();
        services.AddScoped<LoteMobileEstadoReconciler>();
        services.AddScoped<BatchLinker>();
        return services.BuildServiceProvider();
    }
}

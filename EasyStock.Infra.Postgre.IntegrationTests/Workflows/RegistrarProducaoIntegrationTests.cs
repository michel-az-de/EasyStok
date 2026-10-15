using EasyStock.Application.DependencyInjection;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.UseCases.Producao;
using EasyStock.Application.UseCases.RegistrarEntradaEstoque;
using EasyStock.Domain.Entities;
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
/// S23 (#1137): producao em porcoes contra Postgres real, via DI de producao (mesmo
/// DbContext scoped para os use cases compostos, como em FinalizarVendaBalcaoIntegrationTests).
/// </summary>
public class RegistrarProducaoIntegrationTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task CriaLoteFinalizadoEEstoqueDaProducao()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();
        var (empresaId, produtoId) = await SeedAsync(StatusProduto.Ativo);
        var dataProducao = DateTime.UtcNow.Date.AddHours(10);

        await using var provider = BuildProductionProvider();
        RegistrarProducaoResult result;
        await using (var scope = provider.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<EasyStockDbContext>().SetMobileTenantContext(empresaId);
            result = await scope.ServiceProvider.GetRequiredService<RegistrarProducaoUseCase>()
                .ExecuteAsync(new RegistrarProducaoCommand(empresaId, null, dataProducao,
                    [new RegistrarProducaoItemInput(produtoId, 2, 500, 5, 10m)]));
        }

        await using var assert = fixture.CreateDbContext();
        assert.SetMobileTenantContext(empresaId);
        var lote = await assert.Set<Lote>().Include(l => l.Itens).Include(l => l.Etiquetas).SingleAsync();
        lote.Codigo.Should().Be(result.CodigoLote);
        lote.EstaFinalizado.Should().BeTrue();
        lote.Itens.Should().ContainSingle(i => i.Quantidade == 2 && i.PesoG == 500);
        lote.Etiquetas.Should().HaveCount(2);

        var item = await assert.Set<ItemEstoque>().SingleAsync();
        item.QuantidadeAtual.Value.Should().Be(2);
        item.ValidadeEm!.DataValidade.Should().Be(dataProducao.AddDays(5).Date);
        item.CodigoLote!.Value.Should().Be(lote.Codigo);

        var mov = await assert.Set<MovimentacaoEstoque>().SingleAsync();
        mov.Tipo.Should().Be(TipoMovimentacaoEstoque.Entrada);
        mov.Natureza.Should().Be(NaturezaMovimentacaoEstoque.Producao);
    }

    [SkippableFact]
    public async Task FalhaNaEntradaNaoDeixaLote()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();
        // Produto inativo: CriarLote e FinalizarLote passam, a entrada de estoque falha dentro da transacao.
        var (empresaId, produtoId) = await SeedAsync(StatusProduto.Inativo);

        await using var provider = BuildProductionProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<EasyStockDbContext>().SetMobileTenantContext(empresaId);
            var act = () => scope.ServiceProvider.GetRequiredService<RegistrarProducaoUseCase>()
                .ExecuteAsync(new RegistrarProducaoCommand(empresaId, null, DateTime.UtcNow,
                    [new RegistrarProducaoItemInput(produtoId, 2, 500, 5, 10m)]));
            await act.Should().ThrowAsync<Exception>();
        }

        await using var assert = fixture.CreateDbContext();
        assert.SetMobileTenantContext(empresaId);
        (await assert.Set<Lote>().CountAsync()).Should().Be(0);
        (await assert.Set<ItemEstoque>().CountAsync()).Should().Be(0);
    }

    [SkippableFact]
    public async Task PratoMarcado_BaixaInsumoNaMesmaTransacao_EFaltaViraDescoberto()
    {
        // D-M2-01 (#1499): 300 g de molho por porção, 2 porções = 600 g; só há 500 g. A produção
        // passa, o molho sai inteiro e os 100 g que faltaram ficam descobertos no lote do insumo.
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();
        var (empresaId, pratoId) = await SeedAsync(StatusProduto.Ativo);
        var molhoId = await SeedReceitaAsync(empresaId, pratoId);

        await using var provider = BuildProductionProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<EasyStockDbContext>().SetMobileTenantContext(empresaId);
            await scope.ServiceProvider.GetRequiredService<RegistrarEntradaEstoqueUseCase>().ExecuteAsync(
                new RegistrarEntradaEstoqueCommand(empresaId, molhoId, null, 500, 0.03m, null, DateTime.UtcNow.AddDays(-1),
                    NaturezaMovimentacaoEstoque.Compra, null, null, null, null, null, null, null, null, null, null, null, null, null));
        }

        RegistrarProducaoResult result;
        await using (var scope = provider.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<EasyStockDbContext>().SetMobileTenantContext(empresaId);
            result = await scope.ServiceProvider.GetRequiredService<RegistrarProducaoUseCase>()
                .ExecuteAsync(new RegistrarProducaoCommand(empresaId, null, DateTime.UtcNow,
                    [new RegistrarProducaoItemInput(pratoId, 2, 500, 5, 10m)]));
        }

        result.Avisos.Should().ContainSingle().Which.Should().Contain("Faltou 100 G de Molho");

        await using var assert = fixture.CreateDbContext();
        assert.SetMobileTenantContext(empresaId);
        var molho = await assert.Set<ItemEstoque>().SingleAsync(i => i.ProdutoId == molhoId);
        molho.QuantidadeAtual.Value.Should().Be(0);
        molho.QuantidadeDescoberta.Value.Should().Be(100);
        var saida = await assert.Set<MovimentacaoEstoque>().SingleAsync(m => m.ProdutoId == molhoId && m.Tipo == TipoMovimentacaoEstoque.Saida);
        saida.Natureza.Should().Be(NaturezaMovimentacaoEstoque.UsoInterno);
        saida.Quantidade.Value.Should().Be(600);
        (await assert.Set<ItemEstoque>().SingleAsync(i => i.ProdutoId == pratoId)).QuantidadeAtual.Value.Should().Be(2);
    }

    [SkippableFact]
    public async Task PratoSemMarca_BaixaSoAEmbalagemDaReceita()
    {
        // M2.7 (#1523, D-M2-03): 1 bandeja por porção, 2 porções → 2 bandejas; o molho não desce.
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();
        var (empresaId, pratoId) = await SeedAsync(StatusProduto.Ativo);
        var molhoId = await SeedReceitaAsync(empresaId, pratoId);
        var bandejaId = Guid.NewGuid();
        await using (var seed = fixture.CreateDbContext())
        {
            seed.SetMobileTenantContext(empresaId);
            var prato = await seed.Set<Produto>().SingleAsync(p => p.Id == pratoId);
            prato.BaixaInsumoAutomatica = false;
            seed.Set<Produto>().Add(new Produto
            {
                Id = bandejaId, EmpresaId = empresaId, CategoriaId = prato.CategoriaId, Nome = "Bandeja 800 g",
                Status = StatusProduto.Ativo, EhInsumo = true, EhEmbalagem = true, UnidadeMedidaBase = UnidadeMedida.Un,
                CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow
            });
            seed.Set<ProdutoComposicao>().Add(new ProdutoComposicao
            {
                Id = Guid.NewGuid(), EmpresaId = empresaId, ProdutoFinalId = pratoId, InsumoId = bandejaId,
                Quantidade = 1, Unidade = UnidadeMedida.Un, OrdemExibicao = 1, CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow
            });
            await seed.SaveChangesAsync();
        }

        await using var provider = BuildProductionProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<EasyStockDbContext>().SetMobileTenantContext(empresaId);
            var entrada = scope.ServiceProvider.GetRequiredService<RegistrarEntradaEstoqueUseCase>();
            foreach (var (id, qtd) in new[] { (molhoId, 1000m), (bandejaId, 20m) })
                await entrada.ExecuteAsync(new RegistrarEntradaEstoqueCommand(empresaId, id, null, qtd, 0.1m, null, DateTime.UtcNow.AddDays(-1),
                    NaturezaMovimentacaoEstoque.Compra, null, null, null, null, null, null, null, null, null, null, null, null, null));
        }
        await using (var scope = provider.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<EasyStockDbContext>().SetMobileTenantContext(empresaId);
            (await scope.ServiceProvider.GetRequiredService<RegistrarProducaoUseCase>()
                .ExecuteAsync(new RegistrarProducaoCommand(empresaId, null, DateTime.UtcNow,
                    [new RegistrarProducaoItemInput(pratoId, 2, 500, 5, 10m)]))).Avisos.Should().BeEmpty();
        }

        await using var assert = fixture.CreateDbContext();
        assert.SetMobileTenantContext(empresaId);
        (await assert.Set<ItemEstoque>().SingleAsync(i => i.ProdutoId == bandejaId)).QuantidadeAtual.Value.Should().Be(18);
        (await assert.Set<ItemEstoque>().SingleAsync(i => i.ProdutoId == molhoId)).QuantidadeAtual.Value.Should().Be(1000, "o prato não está marcado");
    }

    private async Task<Guid> SeedReceitaAsync(Guid empresaId, Guid pratoId)
    {
        await using var seed = fixture.CreateDbContext();
        seed.SetMobileTenantContext(empresaId);
        var prato = await seed.Set<Produto>().SingleAsync(p => p.Id == pratoId);
        prato.BaixaInsumoAutomatica = true;
        prato.RendimentoBase = 1;
        prato.RendimentoUnidade = UnidadeMedida.Un;
        var molho = new Produto
        {
            Id = Guid.NewGuid(), EmpresaId = empresaId, CategoriaId = prato.CategoriaId, Nome = "Molho",
            Status = StatusProduto.Ativo, EhInsumo = true, UnidadeMedidaBase = UnidadeMedida.G,
            CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow
        };
        seed.Set<Produto>().Add(molho);
        seed.Set<ProdutoComposicao>().Add(new ProdutoComposicao
        {
            Id = Guid.NewGuid(), EmpresaId = empresaId, ProdutoFinalId = pratoId, InsumoId = molho.Id,
            Quantidade = 300, Unidade = UnidadeMedida.G, CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow
        });
        await seed.SaveChangesAsync();
        return molho.Id;
    }

    private async Task<(Guid EmpresaId, Guid ProdutoId)> SeedAsync(StatusProduto status)
    {
        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        await using var seed = fixture.CreateDbContext();
        seed.Set<Empresa>().Add(new Empresa
        {
            Id = empresaId,
            Nome = "Empresa Producao",
            Documento = $"{Random.Shared.Next(100000, 999999)}",
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        });
        seed.Set<Categoria>().Add(new Categoria
        {
            Id = categoriaId,
            EmpresaId = empresaId,
            Nome = "Congelados",
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        });
        seed.Set<Produto>().Add(new Produto
        {
            Id = produtoId,
            EmpresaId = empresaId,
            CategoriaId = categoriaId,
            Nome = "Lasanha 500 g",
            Status = status,
            TipoEmbalagem = TipoEmbalagem.Embalado,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        });
        await seed.SaveChangesAsync();
        return (empresaId, produtoId);
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
        return services.BuildServiceProvider();
    }
}

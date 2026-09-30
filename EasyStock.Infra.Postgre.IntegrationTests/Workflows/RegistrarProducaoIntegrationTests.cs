using EasyStock.Application.DependencyInjection;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.UseCases.Producao;
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

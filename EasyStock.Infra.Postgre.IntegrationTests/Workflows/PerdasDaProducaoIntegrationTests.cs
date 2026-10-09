using EasyStock.Application.DependencyInjection;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.UseCases.Atendimento.Producao;
using EasyStock.Application.UseCases.EstornarSaida;
using EasyStock.Application.UseCases.RegistrarEntradaEstoque;
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
/// M2.6 (#1511): perda contra Postgres real, via DI de produção. O lote vencido aparece como
/// sugestão, sai como Vencido com o custo dele, a perda no preparo sai por FEFO e o estorno a
/// tira do total do resumo.
/// </summary>
public class PerdasDaProducaoIntegrationTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task VencidoSugerido_Perdas_EEstorno_FecharNoResumo()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();
        var (empresaId, produtoId) = await SeedAsync();
        await using var provider = BuildProductionProvider();

        await Escopo(provider, empresaId, async sp =>
        {
            var entrada = sp.GetRequiredService<RegistrarEntradaEstoqueUseCase>();
            await entrada.ExecuteAsync(Entrada(empresaId, produtoId, 5, 4m, DateTime.UtcNow.AddDays(-3), "LOT-VENCIDO"));
            await entrada.ExecuteAsync(Entrada(empresaId, produtoId, 5, 6m, DateTime.UtcNow.AddDays(5), "LOT-BOM"));
        });

        LoteVencido vencido = null!;
        await Escopo(provider, empresaId, async sp =>
        {
            var vencidos = await sp.GetRequiredService<PerdasDaProducaoUseCase>().VencidosAsync(empresaId);
            vencido = vencidos.Should().ContainSingle().Subject;
            (vencido.Lote, vencido.Quantidade, vencido.Valor).Should().Be(("LOT-VENCIDO", 5m, 20m));
        });

        await Escopo(provider, empresaId, async sp => (await sp.GetRequiredService<PerdasDaProducaoUseCase>().LancarAsync(empresaId, false,
            new LancarPerdaInput(produtoId, vencido.ItemEstoqueId, 5, MotivoPerda.Vencido))).Valor.Should().Be(20m));

        PerdaLancada preparo = null!;
        await Escopo(provider, empresaId, async sp => preparo = await sp.GetRequiredService<PerdasDaProducaoUseCase>().LancarAsync(empresaId, false,
            new LancarPerdaInput(produtoId, null, 2, MotivoPerda.PerdaNoPreparo)));
        preparo.Valor.Should().Be(12m, "o vencido já zerou; sai do LOT-BOM a R$ 6");

        await Escopo(provider, empresaId, async sp => await sp.GetRequiredService<EstornarSaidaUseCase>()
            .ExecuteAsync(new EstornarSaidaCommand(empresaId, preparo.Movimentacoes.Single(), "lançado errado")));

        await Escopo(provider, empresaId, async sp =>
        {
            var perdas = sp.GetRequiredService<PerdasDaProducaoUseCase>();
            var resumo = await perdas.ResumoAsync(empresaId);
            resumo.Valor.Should().Be(20m, "a perda no preparo foi desfeita");
            resumo.PorMotivo.Should().ContainSingle().Which.Motivo.Should().Be(MotivoPerda.Vencido);
            resumo.Lancamentos.Should().HaveCount(2).And.ContainSingle(l => l.Desfeita && l.Motivo == MotivoPerda.PerdaNoPreparo);
            (await perdas.VencidosAsync(empresaId)).Should().BeEmpty("o lote vencido já saiu");
        });

        await using var assert = fixture.CreateDbContext();
        assert.SetMobileTenantContext(empresaId);
        (await assert.Set<ItemEstoque>().SingleAsync(i => i.CodigoLote == CodigoLote.From("LOT-BOM"))).QuantidadeAtual.Value
            .Should().Be(5, "o estorno devolveu as 2 porções");
    }

    private static RegistrarEntradaEstoqueCommand Entrada(Guid empresaId, Guid produtoId, decimal qtd, decimal custo, DateTime validade, string lote) => new(
        EmpresaId: empresaId, ProdutoId: produtoId, ProdutoVariacaoId: null, Quantidade: qtd, CustoUnitario: custo,
        PrecoVendaSugerido: null, DataEntrada: DateTime.UtcNow.AddDays(-4), Natureza: NaturezaMovimentacaoEstoque.Producao,
        CodigoInterno: null, CodigoLote: lote, CodigoMarketplace: null, VariacaoDescricao: null, Cor: null, Tamanho: null,
        FornecedorNome: null, Validade: validade, Observacoes: null, DescricaoAnuncio: null, DocumentoReferencia: null,
        DimensoesReais: null, InstrucoesGeracaoDescricao: null, LojaId: null);

    private static async Task Escopo(ServiceProvider provider, Guid empresaId, Func<IServiceProvider, Task> acao)
    {
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<EasyStockDbContext>().SetMobileTenantContext(empresaId);
        await acao(scope.ServiceProvider);
    }

    private async Task<(Guid EmpresaId, Guid ProdutoId)> SeedAsync()
    {
        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        await using var seed = fixture.CreateDbContext();
        seed.Set<Empresa>().Add(new Empresa
        {
            Id = empresaId, Nome = "Empresa Perdas", Documento = $"{Random.Shared.Next(100000, 999999)}",
            CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow
        });
        seed.Set<Categoria>().Add(new Categoria
        {
            Id = categoriaId, EmpresaId = empresaId, Nome = "Congelados", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow
        });
        seed.Set<Produto>().Add(new Produto
        {
            Id = produtoId, EmpresaId = empresaId, CategoriaId = categoriaId, Nome = "Lasanha 500 g",
            Status = StatusProduto.Ativo, CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow
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

using EasyStock.Application.DependencyInjection;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.UseCases.Atendimento.Producao;
using EasyStock.Application.UseCases.Inventario.Desacertos;
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
/// #1542 (M1.4d): o saldo de prato com porções é de cada porção. Contra Postgres real, via DI de
/// produção: a contagem de uma porção só mexe nos lotes dela, e a perda de uma porção sai dos lotes
/// dela (o FEFO em SQL filtra pela variação).
/// </summary>
public class AjustePorPorcaoIntegrationTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task ContagemEPerdaDeUmaPorcao_NaoMexemNaOutra()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();
        var (empresaId, produtoId, p300, p800) = await SeedAsync();
        await using var provider = BuildProductionProvider();

        await Escopo(provider, empresaId, async sp =>
        {
            var entrada = sp.GetRequiredService<RegistrarEntradaEstoqueUseCase>();
            await entrada.ExecuteAsync(Entrada(empresaId, produtoId, p300, 4, "LOT-300"));
            await entrada.ExecuteAsync(Entrada(empresaId, produtoId, p800, 3, "LOT-800"));
        });

        // Ela contou 6 de 300 g. A de 800 g não foi contada.
        await Escopo(provider, empresaId, async sp =>
        {
            var r = await sp.GetRequiredService<AjustarSaldoRapidoUseCase>().ExecuteAsync(new AjustarSaldoRapidoInput(
                empresaId, Guid.NewGuid(), produtoId, null, 6m, "contei o congelador", new PorcaoDoAjuste(p300)));
            r.QuantidadeAtual.Should().Be(9m, "6 de 300 g e 3 de 800 g");
        });
        (await SaldoAsync(empresaId, "LOT-300")).Should().Be(6m);
        (await SaldoAsync(empresaId, "LOT-800")).Should().Be(3m);

        // Uma de 800 g caiu no chão: sai do lote de 800 g.
        await Escopo(provider, empresaId, async sp => await sp.GetRequiredService<PerdasDaProducaoUseCase>().LancarAsync(
            empresaId, false, new LancarPerdaInput(produtoId, null, 1, MotivoPerda.PerdaNoPreparo, ProdutoVariacaoId: p800)));
        (await SaldoAsync(empresaId, "LOT-300")).Should().Be(6m);
        (await SaldoAsync(empresaId, "LOT-800")).Should().Be(2m);

        // Sem dizer a porção, a perda não adivinha: pede a porção.
        var semPorcao = () => Escopo(provider, empresaId, async sp => await sp.GetRequiredService<PerdasDaProducaoUseCase>().LancarAsync(
            empresaId, false, new LancarPerdaInput(produtoId, null, 1, MotivoPerda.PerdaNoPreparo)));
        await semPorcao.Should().ThrowAsync<EasyStock.Application.UseCases.Common.UseCaseValidationException>().WithMessage("*porção*");

        await using var assert = fixture.CreateDbContext();
        assert.SetMobileTenantContext(empresaId);
        var ajustes = await assert.Set<MovimentacaoEstoque>().AsNoTracking()
            .Where(m => m.EmpresaId == empresaId && m.Natureza == NaturezaMovimentacaoEstoque.Ajuste).ToListAsync();
        ajustes.Should().ContainSingle("só o lote de 300 g foi ajustado")
            .Which.ProdutoVariacaoId.Should().Be(p300);
    }

    private async Task<decimal> SaldoAsync(Guid empresaId, string lote)
    {
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresaId);
        return (await db.Set<ItemEstoque>().AsNoTracking().SingleAsync(i => i.CodigoLote == CodigoLote.From(lote))).QuantidadeAtual.Value;
    }

    private static RegistrarEntradaEstoqueCommand Entrada(Guid empresaId, Guid produtoId, Guid porcao, decimal qtd, string lote) => new(
        EmpresaId: empresaId, ProdutoId: produtoId, ProdutoVariacaoId: porcao, Quantidade: qtd, CustoUnitario: 4m,
        PrecoVendaSugerido: null, DataEntrada: DateTime.UtcNow.AddDays(-1), Natureza: NaturezaMovimentacaoEstoque.Producao,
        CodigoInterno: null, CodigoLote: lote, CodigoMarketplace: null, VariacaoDescricao: null, Cor: null, Tamanho: null,
        FornecedorNome: null, Validade: DateTime.UtcNow.AddDays(5), Observacoes: null, DescricaoAnuncio: null, DocumentoReferencia: null,
        DimensoesReais: null, InstrucoesGeracaoDescricao: null, LojaId: null);

    private static async Task Escopo(ServiceProvider provider, Guid empresaId, Func<IServiceProvider, Task> acao)
    {
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<EasyStockDbContext>().SetMobileTenantContext(empresaId);
        await acao(scope.ServiceProvider);
    }

    private async Task<(Guid EmpresaId, Guid ProdutoId, Guid P300, Guid P800)> SeedAsync()
    {
        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        var p300 = Guid.NewGuid();
        var p800 = Guid.NewGuid();
        await using var seed = fixture.CreateDbContext();
        seed.Set<Empresa>().Add(new Empresa
        {
            Id = empresaId, Nome = "Empresa Porções", Documento = $"{Random.Shared.Next(100000, 999999)}",
            CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow
        });
        seed.Set<Categoria>().Add(new Categoria
        {
            Id = categoriaId, EmpresaId = empresaId, Nome = "Congelados", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow
        });
        seed.Set<Produto>().Add(new Produto
        {
            Id = produtoId, EmpresaId = empresaId, CategoriaId = categoriaId, Nome = "Ravióli",
            Status = StatusProduto.Ativo, CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow
        });
        foreach (var (id, nome) in new[] { (p300, "300 g"), (p800, "800 g") })
            seed.Set<ProdutoVariacao>().Add(new ProdutoVariacao
            {
                Id = id, EmpresaId = empresaId, ProdutoId = produtoId, Nome = nome, Ativa = true,
                CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow
            });
        await seed.SaveChangesAsync();
        return (empresaId, produtoId, p300, p800);
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

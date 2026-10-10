using EasyStock.Application.DependencyInjection;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.UseCases.Atendimento.Comanda;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.DependencyInjection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Infra.Postgre.IntegrationTests.Workflows;

/// <summary>
/// M1.4a (#1529): porções do prato pelo console contra Postgres real, via DI. Cada porção de
/// prato ligado ao estoque fica com a sua variação do estoque (D-M1-03); trocar o rótulo de uma
/// porção para o de outra numa edição só passa (unicidade DEFERRABLE) e o vínculo não se perde.
/// </summary>
public class PorcoesDoItemIntegrationTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task PorcoesGravam_GanhamVariacaoDoEstoque_EOVinculoSobreviveAEdicao()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();
        var (empresaId, produtoId, itemId) = await SeedAsync();
        await using var provider = BuildProductionProvider();

        await Escopo(provider, empresaId, sp => sp.GetRequiredService<ItensDoCardapioComandaUseCase>().EditarAsync(empresaId, itemId,
            new DadosItemCardapio(null, null, null, null, null, Porcoes:
            [
                new PorcaoDoItem(null, "300 g", 28m, "300 g", Padrao: true),
                new PorcaoDoItem(null, "800 g", 62m, "800 g"),
            ])));

        DetalheItemCardapio detalhe = null!;
        await Escopo(provider, empresaId, async sp => detalhe = await sp.GetRequiredService<ItensDoCardapioComandaUseCase>().ObterAsync(empresaId, itemId));
        detalhe.Porcoes!.Should().HaveCount(2).And.OnlyContain(p => p.ProdutoVariacaoId != null);

        // Troca os rótulos entre as duas porções numa edição só (a constraint é checada no COMMIT).
        var p300 = detalhe.Porcoes!.Single(p => p.Rotulo == "300 g");
        var p800 = detalhe.Porcoes!.Single(p => p.Rotulo == "800 g");
        await Escopo(provider, empresaId, sp => sp.GetRequiredService<ItensDoCardapioComandaUseCase>().EditarAsync(empresaId, itemId,
            new DadosItemCardapio(null, null, null, null, null, Porcoes:
            [
                new PorcaoDoItem(p300.Id, "800 g", 62m, Padrao: true),
                new PorcaoDoItem(p800.Id, "300 g", 28m),
            ])));

        await using var assert = fixture.CreateDbContext();
        assert.SetMobileTenantContext(empresaId);
        var variacoes = await assert.Set<ProdutoVariacao>().Where(v => v.ProdutoId == produtoId).ToListAsync();
        variacoes.Should().HaveCount(2, "editar não cria variação nova");
        var porcoes = await assert.Set<CardapioItemVariacao>().Where(v => v.CardapioItemId == itemId).ToListAsync();
        porcoes.Single(v => v.Id == p300.Id).ProdutoVariacaoId.Should().Be(p300.ProdutoVariacaoId, "o vínculo segue a porção, não o rótulo");
        variacoes.Single(v => v.Id == p300.ProdutoVariacaoId).Nome.Should().Be("800 g");
    }

    [SkippableFact]
    public async Task OItemQueOCheckoutCarrega_VemComAsPorcoes_EAEsgotadaRecusa()
    {
        // M1.4b (#1531): o checkout cobra o preço da porção; a leitura dele precisa trazer as porções.
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();
        var (empresaId, _, itemId) = await SeedAsync();
        await using var provider = BuildProductionProvider();
        await Escopo(provider, empresaId, sp => sp.GetRequiredService<ItensDoCardapioComandaUseCase>().EditarAsync(empresaId, itemId,
            new DadosItemCardapio(null, null, null, null, null, Porcoes:
            [
                new PorcaoDoItem(null, "300 g", 28m, Padrao: true),
                new PorcaoDoItem(null, "800 g", 62m, Disponivel: false),
            ])));

        await Escopo(provider, empresaId, async sp =>
        {
            var db = sp.GetRequiredService<EasyStockDbContext>();
            var storefrontId = await db.Set<CardapioItem>().Where(i => i.Id == itemId).Select(i => i.StorefrontId).SingleAsync();
            var item = await sp.GetRequiredService<EasyStock.Application.Ports.Output.Persistence.Storefront.ICardapioItemRepository>()
                .GetByIdAsync(storefrontId, itemId);

            item!.Variacoes.Should().HaveCount(2);
            var padrao = item.PorcaoParaVenda(null)!;
            (padrao.Rotulo, padrao.PrecoStorefront).Should().Be(("300 g", 28m));
            padrao.ProdutoVariacaoId.Should().NotBeNull("a linha do pedido grava a variação do estoque");
            var esgotada = () => item.PorcaoParaVenda(item.Variacoes.Single(v => v.Rotulo == "800 g").Id);
            esgotada.Should().Throw<EasyStock.Domain.Exceptions.RegraDeDominioVioladaException>().WithMessage("*esgotad*");
        });
    }

    [SkippableFact]
    public async Task ProduzirDuasPorcoes_EVenderUma_BaixaSoDoSaldoDela()
    {
        // M1.4c (#1537, D-M1-03): 6 de 800 g e 4 de 300 g; um pedido de 2 de 800 g deixa 4 e 4.
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();
        var (empresaId, produtoId, itemId) = await SeedAsync();
        await using var provider = BuildProductionProvider();
        await Escopo(provider, empresaId, sp => sp.GetRequiredService<ItensDoCardapioComandaUseCase>().EditarAsync(empresaId, itemId,
            new DadosItemCardapio(null, null, null, null, null, Porcoes:
            [new PorcaoDoItem(null, "300 g", 28m, Padrao: true), new PorcaoDoItem(null, "800 g", 62m)])));
        DetalheItemCardapio detalhe = null!;
        await Escopo(provider, empresaId, async sp => detalhe = await sp.GetRequiredService<ItensDoCardapioComandaUseCase>().ObterAsync(empresaId, itemId));
        var p300 = detalhe.Porcoes!.Single(p => p.Rotulo == "300 g");
        var p800 = detalhe.Porcoes!.Single(p => p.Rotulo == "800 g");

        await Escopo(provider, empresaId, sp => sp.GetRequiredService<EasyStock.Application.UseCases.Atendimento.Producao.ProduzirPratosUseCase>()
            .ExecuteAsync(new EasyStock.Application.UseCases.Atendimento.Producao.ProduzirPratosCommand(empresaId, Guid.Empty, "Thati",
            [
                new EasyStock.Application.UseCases.Atendimento.Producao.PratoProduzidoInput(itemId, 6, 800, null, 5, p800.Id),
                new EasyStock.Application.UseCases.Atendimento.Producao.PratoProduzidoInput(itemId, 4, 300, null, 5, p300.Id),
            ])));

        Guid pedidoId;
        await using (var seed = fixture.CreateDbContext())
        {
            seed.SetMobileTenantContext(empresaId);
            var pedido = Pedido.Criar(empresaId, cliente: null, lojaId: null, "atendimento");
            pedido.Itens.Add(new PedidoItem
            {
                Id = Guid.NewGuid(), PedidoId = pedido.Id, ProdutoId = produtoId, CardapioItemId = itemId, CardapioItemVariacaoId = p800.Id,
                ProdutoVariacaoId = p800.ProdutoVariacaoId, VariacaoRotuloSnapshot = "800 g", Nome = "Ravióli", Quantidade = 2,
                PrecoUnitario = 62m, Subtotal = 124m, CriadoEm = DateTime.UtcNow
            });
            pedido.RecalcularTotal();
            seed.Pedidos.Add(pedido);
            pedidoId = pedido.Id;
            await seed.SaveChangesAsync();
        }
        await Escopo(provider, empresaId, async sp =>
        {
            var status = sp.GetRequiredService<EasyStock.Application.UseCases.AtualizarStatusPedido.AtualizarStatusPedidoUseCase>();
            await status.ExecuteAsync(new(empresaId, pedidoId, "preparando", null, null, "web"));
            await status.ExecuteAsync(new(empresaId, pedidoId, "pronto", null, null, "web"));
        });

        await Escopo(provider, empresaId, async sp =>
        {
            var estoque = await sp.GetRequiredService<EasyStock.Application.UseCases.Atendimento.Producao.EstoqueDoDiaUseCase>().ExecuteAsync(empresaId);
            var prato = estoque.Pratos.Single(p => p.CardapioItemId == itemId);
            prato.Porcoes!.Select(p => (p.Rotulo, p.Saldo)).Should().Equal(("300 g", 4m), ("800 g", 4m));
            prato.Saldo.Should().Be(8);
        });

        await using var assert = fixture.CreateDbContext();
        assert.SetMobileTenantContext(empresaId);
        var venda = await assert.Set<MovimentacaoEstoque>().SingleAsync(m => m.Natureza == NaturezaMovimentacaoEstoque.Venda);
        venda.ProdutoVariacaoId.Should().Be(p800.ProdutoVariacaoId);
    }

    private static async Task Escopo(ServiceProvider provider, Guid empresaId, Func<IServiceProvider, Task> acao)
    {
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<EasyStockDbContext>().SetMobileTenantContext(empresaId);
        await acao(scope.ServiceProvider);
    }

    private async Task<(Guid EmpresaId, Guid ProdutoId, Guid ItemId)> SeedAsync()
    {
        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        var agora = DateTime.UtcNow;
        await using var seed = fixture.CreateDbContext();
        seed.SetMobileTenantContext(empresaId);
        seed.Set<Empresa>().Add(new Empresa
        {
            Id = empresaId, Nome = "Empresa Porções", Documento = $"{Random.Shared.Next(100000, 999999)}", CriadoEm = agora, AlteradoEm = agora
        });
        seed.Set<Categoria>().Add(new Categoria { Id = categoriaId, EmpresaId = empresaId, Nome = "Massas", CriadoEm = agora, AlteradoEm = agora });
        var produto = new Produto
        {
            Id = Guid.NewGuid(), EmpresaId = empresaId, CategoriaId = categoriaId, Nome = "Ravióli", Status = StatusProduto.Ativo,
            PrecoReferencia = EasyStock.Domain.ValueObjects.Dinheiro.FromDecimal(30m), CriadoEm = agora, AlteradoEm = agora
        };
        seed.Set<Produto>().Add(produto);
        var vitrine = StorefrontEntity.Criar(empresaId, $"sf-porc-{Guid.NewGuid():N}"[..30], "Vitrine", 0m);
        vitrine.Ativar();
        seed.Storefronts.Add(vitrine);
        var item = CardapioItem.CriarAPartirDeProduto(vitrine.Id, produto);
        seed.Set<CardapioItem>().Add(item);
        await seed.SaveChangesAsync();
        return (empresaId, produto.Id, item.Id);
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

using EasyStock.Api.Mobile.Services;
using EasyStock.Application.DependencyInjection;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Lookup;
using EasyStock.Application.UseCases.AtualizarStatusPedido;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
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
/// #1619: o prato incluído pelo console nasce avulso (sem produto). A cliente encomenda, o item do pedido grava
/// <c>ProdutoId = null</c>, e só depois a produção liga o prato ao estoque. No Pronto, a baixa achava o item sem
/// produto e pulava: o saldo ficava 10 quando devia ser 9, sem alerta. Também a porção ligada depois baixava do
/// lote que vence primeiro, de qualquer porção.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class PratoLigadoDepoisDoPedidoIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    [SkippableFact]
    public async Task PratoLigadoDepois_ProntoBaixaUm_ECancelarDevolve()
    {
        var s = await SemearAsync();
        await using var provider = BuildProductionProvider();
        var pedidoId = await EncomendarAsync(s, porcaoId: null);
        await LigarAoEstoqueAsync(s);

        await MudarStatusAsync(provider, s, pedidoId, "preparando", "pronto");
        (await SaldosAsync(s)).Should().Equal(new Dictionary<string, decimal> { ["P500"] = 5m, ["P800"] = 4m },
            "sem porção no pedido, a baixa é FEFO no prato e o lote de 800 g vence primeiro");

        await MudarStatusAsync(provider, s, pedidoId, "cancelado");
        (await SaldosAsync(s)).Should().Equal(new Dictionary<string, decimal> { ["P500"] = 5m, ["P800"] = 5m },
            "a devolução acha a saída pelo produto que o item herdou");
    }

    [SkippableFact]
    public async Task PorcaoLigadaDepois_BaixaDoLoteDaPropriaPorcao()
    {
        var s = await SemearAsync();
        await using var provider = BuildProductionProvider();
        var pedidoId = await EncomendarAsync(s, porcaoId: s.Porcao500Id);
        await LigarAoEstoqueAsync(s);

        await MudarStatusAsync(provider, s, pedidoId, "preparando", "pronto");

        (await SaldosAsync(s)).Should().Equal(new Dictionary<string, decimal> { ["P500"] = 4m, ["P800"] = 5m },
            "a porção de 500 g sai do lote de 500 g, mesmo com o de 800 g vencendo antes");
    }

    private sealed record Semente(Guid EmpresaId, Guid ProdutoId, Guid PratoId, Guid Porcao500Id, Guid Porcao800Id,
        Guid Variacao500Id, Guid Variacao800Id);

    /// <summary>Prato avulso com duas porções sem vínculo; o produto e um lote por porção já existem.</summary>
    private async Task<Semente> SemearAsync()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();
        var agora = DateTime.UtcNow;
        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        var v500 = Guid.NewGuid();
        var v800 = Guid.NewGuid();
        await using var db = fixture.CreateDbContext();
        db.Set<Empresa>().Add(new Empresa
        {
            Id = empresaId, Nome = "Empresa 1619", Documento = $"{Random.Shared.Next(100000, 999999)}", CriadoEm = agora, AlteradoEm = agora
        });
        db.Set<Categoria>().Add(new Categoria { Id = categoriaId, EmpresaId = empresaId, Nome = "Massas", CriadoEm = agora, AlteradoEm = agora });
        db.Set<Produto>().Add(new Produto
        {
            Id = produtoId, EmpresaId = empresaId, CategoriaId = categoriaId, Nome = "Lasanha", Status = StatusProduto.Ativo,
            CriadoEm = agora, AlteradoEm = agora
        });
        foreach (var (id, nome) in new[] { (v500, "500 g"), (v800, "800 g") })
            db.Set<ProdutoVariacao>().Add(new ProdutoVariacao
            {
                Id = id, EmpresaId = empresaId, ProdutoId = produtoId, Nome = nome, Ativa = true, CriadoEm = agora, AlteradoEm = agora
            });
        foreach (var (rotulo, variacao, dias) in new[] { ("P500", v500, 20), ("P800", v800, 5) })
            db.Set<ItemEstoque>().Add(new ItemEstoque
            {
                Id = Guid.NewGuid(), EmpresaId = empresaId, ProdutoId = produtoId, ProdutoVariacaoId = variacao, CodigoInterno = rotulo,
                QuantidadeInicial = Quantidade.From(5m), QuantidadeAtual = Quantidade.From(5m), CustoUnitario = Dinheiro.FromDecimal(4m),
                Status = StatusItemEstoque.Ok, ValidadeEm = Validade.From(agora.Date.AddDays(dias)),
                EntradaEm = agora.AddDays(-1), CriadoEm = agora.AddDays(-1), AlteradoEm = agora.AddDays(-1)
            });
        var storefront = StorefrontEntity.Criar(empresaId, $"loja-1619-{Guid.NewGuid():N}"[..20], "Loja 1619", 0m);
        storefront.Ativar();
        db.Storefronts.Add(storefront);
        var prato = CardapioItem.CriarAvulso(storefront.Id, "Lasanha", 30m, "Massas");
        var p500 = CardapioItemVariacao.Criar(prato.Id, "500 g", 30m);
        var p800 = CardapioItemVariacao.Criar(prato.Id, "800 g", 45m, ordemExibicao: 1, ehPadrao: true);
        prato.AdicionarVariacao(p500);
        prato.AdicionarVariacao(p800);
        db.CardapioItens.Add(prato);
        await db.SaveChangesAsync();
        return new Semente(empresaId, produtoId, prato.Id, p500.Id, p800.Id, v500, v800);
    }

    /// <summary>O pedido grava o prato como estava: sem produto e sem a porção do estoque.</summary>
    private async Task<Guid> EncomendarAsync(Semente s, Guid? porcaoId)
    {
        var pedido = Pedido.Criar(s.EmpresaId);
        pedido.Itens.Add(new PedidoItem
        {
            Id = Guid.NewGuid(), PedidoId = pedido.Id, ProdutoId = null, CardapioItemId = s.PratoId, CardapioItemVariacaoId = porcaoId,
            Nome = "Lasanha", Quantidade = 1m, PrecoUnitario = 30m, Subtotal = 30m, CriadoEm = DateTime.UtcNow
        });
        pedido.RecalcularTotal();
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(s.EmpresaId);
        db.Pedidos.Add(pedido);
        await db.SaveChangesAsync();
        return pedido.Id;
    }

    /// <summary>O que a produção faz depois (LigarPratoAoEstoque, ProduzirPratos): prato e porções ganham o vínculo.</summary>
    private async Task LigarAoEstoqueAsync(Semente s)
    {
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(s.EmpresaId);
        var prato = await db.CardapioItens.Include(c => c.Variacoes).SingleAsync(c => c.Id == s.PratoId);
        prato.VincularProduto(s.ProdutoId);
        prato.Variacoes.Single(v => v.Id == s.Porcao500Id).VincularProdutoVariacao(s.Variacao500Id);
        prato.Variacoes.Single(v => v.Id == s.Porcao800Id).VincularProdutoVariacao(s.Variacao800Id);
        await db.SaveChangesAsync();
    }

    private static async Task MudarStatusAsync(ServiceProvider provider, Semente s, Guid pedidoId, params string[] status)
    {
        foreach (var novo in status)
        {
            await using var scope = provider.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<EasyStockDbContext>().SetMobileTenantContext(s.EmpresaId);
            await scope.ServiceProvider.GetRequiredService<AtualizarStatusPedidoUseCase>().ExecuteAsync(
                new AtualizarStatusPedidoCommand(s.EmpresaId, pedidoId, novo, UsuarioNome: "Bia", NivelSolicitante: NivelAcesso.Gerente));
        }
    }

    private async Task<Dictionary<string, decimal>> SaldosAsync(Semente s)
    {
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(s.EmpresaId);
        return (await db.ItensEstoque.Where(i => i.ProdutoId == s.ProdutoId).ToListAsync())
            .ToDictionary(i => i.CodigoInterno!, i => i.QuantidadeAtual.Value);
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
        services.AddSingleton(Substitute.For<ICacheService>());
        services.AddEasyStockPostgreInfrastructure(fixture.ConnectionString, config);
        services.AddEasyStockApplication();
        services.AddSingleton(Substitute.For<ICepLookupClient>());
        services.AddSingleton(Substitute.For<IGeocodingClient>());
        services.AddScoped<MobileSystemUserResolver>();
        services.AddScoped<MobileStockReconciler>();
        return services.BuildServiceProvider();
    }
}

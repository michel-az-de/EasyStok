using EasyStock.Api.Mobile.Services;
using EasyStock.Api.Mobile.Services.Linkers;
using EasyStock.Application.DependencyInjection;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Lookup;
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
/// #1543: pedido que nasceu no ERP desce para o aparelho com o id do ERP, e o aparelho o devolve
/// quando avança ou cancela. Essa troca de status passa pela esteira do ERP (máquina de estados,
/// estoque pelos itens do pedido, evento), e não por um UPDATE direto na tabela, como era.
/// Contra Postgres real, com o <see cref="OrderLinker"/> resolvido pelo DI.
/// </summary>
public class PedidoDoErpNoPwaIntegrationTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task AparelhoAvancaECancelaPedidoDoConsole_PelaEsteiraDoErp()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();
        var s = await SeedAsync();
        await using var provider = BuildProductionProvider();
        await Escopo(provider, s.EmpresaId, sp => sp.GetRequiredService<RegistrarEntradaEstoqueUseCase>().ExecuteAsync(
            new RegistrarEntradaEstoqueCommand(s.EmpresaId, s.ProdutoId, null, 10m, 4m, null, DateTime.UtcNow.AddDays(-1),
                NaturezaMovimentacaoEstoque.Compra, null, null, null, null, null, null, null, null, null, null, null, null, null)));

        // O aparelho recebeu o pedido do console e o pôs em preparo, depois deu como pronto.
        await EcoarDoAparelho(provider, s, "preparando");
        (await StatusDoPedido(s)).Should().Be("preparando");
        (await Saldo(s)).Should().Be(10m, "preparar ainda não baixa");

        await EcoarDoAparelho(provider, s, "pronto");
        (await StatusDoPedido(s)).Should().Be("pronto");
        (await Saldo(s)).Should().Be(8m, "a esteira baixou as 2 porções do pedido");

        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(s.EmpresaId);
            var vendas = await db.Set<MovimentacaoEstoque>().Where(m => m.Natureza == NaturezaMovimentacaoEstoque.Venda).ToListAsync();
            vendas.Should().ContainSingle("o caminho do aparelho não baixa de novo o que a esteira já baixou")
                .Which.DocumentoReferencia.Should().Be($"{s.PedidoId}:{s.ItemId}", "a saída é a do pedido do ERP, por item");
            (await db.Set<PedidoEvento>().Where(e => e.PedidoId == s.PedidoId && e.Tipo == "status_changed").ToListAsync())
                .Should().HaveCount(2).And.OnlyContain(e => e.Origem == "mobile", "a troca fica na trilha do pedido, com a origem");
        }

        // Reenviar o mesmo status não faz nada.
        await EcoarDoAparelho(provider, s, "pronto");
        (await Saldo(s)).Should().Be(8m);

        // Cancelar no aparelho devolve pelo cancelamento do ERP.
        await EcoarDoAparelho(provider, s, "cancelado");
        (await StatusDoPedido(s)).Should().Be("cancelado");
        (await Saldo(s)).Should().Be(10m);

        // Transição que a máquina de estados não aceita: o ERP fica como está e o sync não quebra.
        await EcoarDoAparelho(provider, s, "preparando");
        (await StatusDoPedido(s)).Should().Be("cancelado");
        (await Saldo(s)).Should().Be(10m);
    }

    private sealed record Semente(Guid EmpresaId, Guid ProdutoId, Guid PedidoId, Guid ItemId);

    /// <summary>O pedido do ERP volta do aparelho com o mesmo id e o status novo; depois roda o vínculo, como no sync.</summary>
    private async Task EcoarDoAparelho(ServiceProvider provider, Semente s, string status)
    {
        var id = s.PedidoId.ToString();
        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(s.EmpresaId);
            var order = await db.Set<Order>().IgnoreQueryFilters().Include(o => o.Items).SingleOrDefaultAsync(o => o.Id == id);
            if (order is null)
            {
                order = new Order
                {
                    Id = id, ClientSnapshotName = "Ana", Total = 60m, LastDeviceId = "dev-a", LastOperatorName = "Thati", EmpresaId = s.EmpresaId
                };
                order.Items.Add(new OrderItem { OrderId = id, ProductId = s.ProdutoId.ToString(), Name = "Lasanha", Qty = 2, UnitPrice = 30m });
                db.Add(order);
            }
            order.Status = status;
            order.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        await Escopo(provider, s.EmpresaId, sp => sp.GetRequiredService<OrderLinker>().ExecuteAsync([id], s.EmpresaId));
    }

    private async Task<string?> StatusDoPedido(Semente s)
    {
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(s.EmpresaId);
        return await db.Set<Pedido>().Where(p => p.Id == s.PedidoId).Select(p => p.Status).SingleAsync();
    }

    private async Task<decimal> Saldo(Semente s)
    {
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(s.EmpresaId);
        return (await db.Set<ItemEstoque>().Where(i => i.ProdutoId == s.ProdutoId).ToListAsync()).Sum(i => i.QuantidadeAtual.Value);
    }

    private static async Task Escopo(ServiceProvider provider, Guid empresaId, Func<IServiceProvider, Task> acao)
    {
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<EasyStockDbContext>().SetMobileTenantContext(empresaId);
        await acao(scope.ServiceProvider);
    }

    private async Task<Semente> SeedAsync()
    {
        var (empresaId, categoriaId, produtoId) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var agora = DateTime.UtcNow;
        await using var seed = fixture.CreateDbContext();
        seed.Set<Empresa>().Add(new Empresa
        {
            Id = empresaId, Nome = "Empresa PWA", Documento = $"{Random.Shared.Next(100000, 999999)}", CriadoEm = agora, AlteradoEm = agora
        });
        seed.Set<Categoria>().Add(new Categoria { Id = categoriaId, EmpresaId = empresaId, Nome = "Massas", CriadoEm = agora, AlteradoEm = agora });
        seed.Set<Produto>().Add(new Produto
        {
            Id = produtoId, EmpresaId = empresaId, CategoriaId = categoriaId, Nome = "Lasanha", Status = StatusProduto.Ativo,
            CriadoEm = agora, AlteradoEm = agora
        });
        var pedido = Pedido.Criar(empresaId, cliente: null, lojaId: null, "console");
        var item = new PedidoItem
        {
            Id = Guid.NewGuid(), PedidoId = pedido.Id, ProdutoId = produtoId, Nome = "Lasanha", Quantidade = 2,
            PrecoUnitario = 30m, Subtotal = 60m, CriadoEm = agora
        };
        pedido.Itens.Add(item);
        pedido.RecalcularTotal();
        seed.Pedidos.Add(pedido);
        await seed.SaveChangesAsync();
        return new Semente(empresaId, produtoId, pedido.Id, item.Id);
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
        // Criar pedido registra o endereço de entrega; CEP e geocoding são registrados pela API (#1560).
        services.AddSingleton(Substitute.For<ICepLookupClient>());
        services.AddSingleton(Substitute.For<IGeocodingClient>());
        services.AddScoped<MobileSystemUserResolver>();
        services.AddScoped<MobileStockReconciler>();
        services.AddScoped<MobileSaleSyncService>();
        services.AddScoped<OrderLinker>();
        return services.BuildServiceProvider();
    }
}

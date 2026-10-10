using EasyStock.Application.DependencyInjection;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.UseCases.AtualizarStatusPedido;
using EasyStock.Application.UseCases.CancelarPedido;
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
/// #1534: o pedido do site e da comanda nasce sem loja e não baixava estoque. Contra Postgres,
/// pelo caminho real (status → pronto, depois cancelar): baixa do lote da empresa e devolve.
/// </summary>
public class PedidoSemLojaBaixaEstoqueIntegrationTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task PedidoSemLoja_AoFicarPronto_BaixaDoEstoqueDaEmpresa_ECancelarDevolve()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();
        var empresaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        var agora = DateTime.UtcNow;
        Guid pedidoId;
        await using (var seed = fixture.CreateDbContext())
        {
            seed.SetMobileTenantContext(empresaId);
            var categoriaId = Guid.NewGuid();
            seed.Set<Empresa>().Add(new Empresa { Id = empresaId, Nome = "Empresa Sem Loja", Documento = $"{Random.Shared.Next(100000, 999999)}", CriadoEm = agora, AlteradoEm = agora });
            seed.Set<Categoria>().Add(new Categoria { Id = categoriaId, EmpresaId = empresaId, Nome = "Massas", CriadoEm = agora, AlteradoEm = agora });
            seed.Set<Produto>().Add(new Produto { Id = produtoId, EmpresaId = empresaId, CategoriaId = categoriaId, Nome = "Lasanha", Status = StatusProduto.Ativo, CriadoEm = agora, AlteradoEm = agora });
            // Como o checkout cria: sem loja.
            var pedido = Pedido.Criar(empresaId, cliente: null, lojaId: null, "atendimento");
            pedido.Itens.Add(new PedidoItem { Id = Guid.NewGuid(), PedidoId = pedido.Id, ProdutoId = produtoId, Nome = "Lasanha", Quantidade = 2, PrecoUnitario = 30m, Subtotal = 60m, CriadoEm = agora });
            pedido.RecalcularTotal();
            seed.Pedidos.Add(pedido);
            pedidoId = pedido.Id;
            await seed.SaveChangesAsync();
        }

        await using var provider = BuildProductionProvider();
        // A produção do console grava o lote sem loja.
        await Escopo(provider, empresaId, sp => sp.GetRequiredService<RegistrarEntradaEstoqueUseCase>().ExecuteAsync(
            new RegistrarEntradaEstoqueCommand(empresaId, produtoId, null, 5, 10m, null, agora.AddDays(-1),
                NaturezaMovimentacaoEstoque.Producao, null, null, null, null, null, null, null, null, null, null, null, null, null)));

        await Escopo(provider, empresaId, async sp =>
        {
            var status = sp.GetRequiredService<AtualizarStatusPedidoUseCase>();
            await status.ExecuteAsync(new AtualizarStatusPedidoCommand(empresaId, pedidoId, "preparando", null, null, "web"));
            await status.ExecuteAsync(new AtualizarStatusPedidoCommand(empresaId, pedidoId, "pronto", null, null, "web"));
        });
        (await Saldo(empresaId, produtoId)).Should().Be(3, "o pedido sem loja baixa 2 do lote da empresa");

        await Escopo(provider, empresaId, sp => sp.GetRequiredService<CancelarPedidoUseCase>().ExecuteAsync(
            new CancelarPedidoCommand(empresaId, pedidoId, Motivo: "cliente desistiu", NivelSolicitante: NivelAcesso.Gerente)));
        (await Saldo(empresaId, produtoId)).Should().Be(5, "cancelar devolve ao lote que baixou");

        await using var assert = fixture.CreateDbContext();
        assert.SetMobileTenantContext(empresaId);
        var naturezas = await assert.Set<MovimentacaoEstoque>().Where(m => m.ProdutoId == produtoId).Select(m => m.Natureza).ToListAsync();
        naturezas.Should().BeEquivalentTo([NaturezaMovimentacaoEstoque.Producao, NaturezaMovimentacaoEstoque.Venda, NaturezaMovimentacaoEstoque.Estorno]);
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

    private ServiceProvider BuildProductionProvider()
    {
        var config = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddMemoryCache();
        services.AddHttpContextAccessor();
        services.AddSingleton(Substitute.For<ICurrentUserAccessor>());
        services.AddSingleton(Substitute.For<EasyStock.Application.Ports.Output.ICacheService>());
        services.AddEasyStockPostgreInfrastructure(fixture.ConnectionString, config);
        services.AddEasyStockApplication();
        return services.BuildServiceProvider();
    }
}

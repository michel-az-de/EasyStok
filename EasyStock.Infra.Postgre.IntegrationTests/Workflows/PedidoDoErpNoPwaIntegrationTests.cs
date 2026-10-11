using EasyStock.Api.Mobile.Services;
using EasyStock.Api.Mobile.Services.Linkers;
using EasyStock.Application.DependencyInjection;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Lookup;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.CancelarPedido;
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
        await AbastecerAsync(provider, s, 10m);

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

    [SkippableFact]
    public async Task AparelhoMandaSoOUltimoStatus_AEsteiraPercorreOCaminho()
    {
        // O aparelho junta na fila os toques feitos sem rede: o pedido que o ERP tem em "aguardando"
        // chega de uma vez como "pronto". A esteira não aceita salto; sem percorrer o caminho, o
        // pedido ficava travado em "aguardando" e sem baixa, e todo reenvio era recusado de novo.
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();
        var s = await SeedAsync();
        await using var provider = BuildProductionProvider();
        await AbastecerAsync(provider, s, 10m);

        await EcoarDoAparelho(provider, s, "pronto");

        (await StatusDoPedido(s)).Should().Be("pronto");
        (await Saldo(s)).Should().Be(8m);
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(s.EmpresaId);
        (await db.Set<PedidoEvento>().Where(e => e.PedidoId == s.PedidoId && e.Tipo == "status_changed")
                .OrderBy(e => e.OcorridoEm).Select(e => e.StatusNovo).ToListAsync())
            .Should().Equal(["preparando", "pronto"], "cada passo fica na trilha do pedido, na ordem em que aconteceu");
    }

    [SkippableFact]
    public async Task AparelhoEntregaEDepoisCancela_OErpAcompanha()
    {
        // Entregar no aparelho registra o recebimento sozinho. Cancelar pedido pago é do gerente no
        // ERP; sem tratar o recebimento que o próprio vínculo criou, o cancelamento do aparelho era
        // sempre recusado e o pedido ficava entregue e pago no ERP, com o aparelho dizendo cancelado.
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();
        var s = await SeedAsync();
        await using var provider = BuildProductionProvider();
        await AbastecerAsync(provider, s, 10m);

        await EcoarDoAparelho(provider, s, "entregue");
        (await StatusDoPedido(s)).Should().Be("entregue", "aguardando → preparando → pronto → entregue, pela esteira");
        (await Saldo(s)).Should().Be(8m);
        (await Recebimentos(s)).Should().ContainSingle().Which.Should().Be(60m);
        (await ObservacoesDasVendas(s)).Should().ContainSingle("a Venda da entrega nasce no vínculo, depois que a esteira aceita")
            .Which.Should().NotStartWith("[CANCELADO");

        await EcoarDoAparelho(provider, s, "cancelado");
        (await StatusDoPedido(s)).Should().Be("cancelado");
        (await Saldo(s)).Should().Be(10m, "o cancelamento do ERP devolve o que a esteira baixou");
        (await ObservacoesDasVendas(s)).Should().ContainSingle().Which.Should().StartWith("[CANCELADO no mobile");

        await EcoarDoAparelho(provider, s, "cancelado");
        (await ObservacoesDasVendas(s)).Single().Should().NotContain("] [CANCELADO", "reenviar o cancelamento não marca a Venda de novo");
    }

    [SkippableTheory]
    [InlineData("Sinal recebido no balcão", false)]
    [InlineData(OrderLinker.MarcaDoRecebimentoAutomatico + " (texto digitado no console)", true)]
    public async Task CancelarNoAparelhoPedidoComRecebimentoLancadoNoErp_SegueARegraDoErp(string observacao, bool lancadoPorUsuario)
    {
        // O aparelho entregou (recebimento automático e Venda) e depois alguém lançou outro recebimento
        // no ERP. Aí o cancelamento do aparelho é recusado, e a Venda não é marcada como cancelada.
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();
        var s = await SeedAsync();
        await using var provider = BuildProductionProvider();
        await AbastecerAsync(provider, s, 10m);
        await EcoarDoAparelho(provider, s, "entregue");
        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(s.EmpresaId);
            db.Add(new PedidoPagamento
            {
                Id = Guid.NewGuid(), PedidoId = s.PedidoId, Metodo = "pix", Valor = 30m, PagoEm = DateTime.UtcNow,
                RegistradoPorNome = "Bia", RegistradoPorUserId = lancadoPorUsuario ? Guid.NewGuid() : null, Observacao = observacao
            });
            await db.SaveChangesAsync();
        }

        await EcoarDoAparelho(provider, s, "cancelado");

        (await StatusDoPedido(s)).Should().Be("entregue", "dinheiro que alguém lançou no ERP só o gerente cancela");
        (await Saldo(s)).Should().Be(8m);
        (await ObservacoesDasVendas(s)).Should().ContainSingle().Which.Should().NotStartWith("[CANCELADO");
    }

    [SkippableFact]
    public async Task EsteiraRecusaAEntrega_NaoRegistraRecebimento()
    {
        // O pedido foi cancelado no console; o aparelho, desatualizado, manda "entregue".
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();
        var s = await SeedAsync();
        await using var provider = BuildProductionProvider();
        await AbastecerAsync(provider, s, 10m);
        await Escopo(provider, s.EmpresaId, sp => sp.GetRequiredService<CancelarPedidoUseCase>()
            .ExecuteAsync(new CancelarPedidoCommand(s.EmpresaId, s.PedidoId, Motivo: "cliente desistiu")));

        await EcoarDoAparelho(provider, s, "entregue");

        (await StatusDoPedido(s)).Should().Be("cancelado");
        (await Recebimentos(s)).Should().BeEmpty("pedido que o ERP não deu como entregue não recebe");
        (await ObservacoesDasVendas(s)).Should().BeEmpty("nem vira Venda");
        (await Saldo(s)).Should().Be(10m);
    }

    [SkippableFact]
    public async Task EsteiraFalhaNoMeioDaBaixa_OPedidoSeguinteNaoGravaAMetade()
    {
        // #1543 (item 4, no vínculo de pedido): a baixa da esteira mexe no lote do primeiro item e
        // estoura no segundo. A falha é engolida; sem descartar o que ficou no contexto, o SaveChanges
        // do pedido seguinte gravava a baixa do primeiro item num pedido que não ficou pronto.
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();
        var s = await SeedAsync();
        var nhoqueId = Guid.NewGuid();
        Guid seguinteId;
        await using (var db = fixture.CreateDbContext())
        {
            var agora = DateTime.UtcNow;
            var categoriaId = await db.Set<Categoria>().IgnoreQueryFilters().Where(c => c.EmpresaId == s.EmpresaId).Select(c => c.Id).SingleAsync();
            db.Set<Produto>().Add(new Produto
            {
                Id = nhoqueId, EmpresaId = s.EmpresaId, CategoriaId = categoriaId, Nome = "Nhoque", Status = StatusProduto.Ativo,
                CriadoEm = agora, AlteradoEm = agora
            });
            // Quantidade acima do teto da baixa: é este item, gravado depois do primeiro, que estoura.
            db.Add(new PedidoItem
            {
                Id = Guid.NewGuid(), PedidoId = s.PedidoId, ProdutoId = nhoqueId, Nome = "Nhoque", Quantidade = 100_000m,
                PrecoUnitario = 1m, Subtotal = 100_000m, CriadoEm = agora
            });
            var seguinte = Pedido.Criar(s.EmpresaId, cliente: null, lojaId: null, "console");
            seguinte.Itens.Add(new PedidoItem
            {
                Id = Guid.NewGuid(), PedidoId = seguinte.Id, ProdutoId = s.ProdutoId, Nome = "Lasanha", Quantidade = 1,
                PrecoUnitario = 30m, Subtotal = 30m, CriadoEm = agora
            });
            seguinte.RecalcularTotal();
            db.Pedidos.Add(seguinte);
            seguinteId = seguinte.Id;
            foreach (var (pedidoId, status) in new[] { (s.PedidoId, "pronto"), (seguinteId, "preparando") })
            {
                var doAparelho = new Order
                {
                    Id = pedidoId.ToString(), ClientSnapshotName = "Ana", Total = 60m, Status = status, LastDeviceId = "dev-a",
                    LastOperatorName = "Thati", EmpresaId = s.EmpresaId, UpdatedAt = agora
                };
                doAparelho.Items.Add(new OrderItem { OrderId = doAparelho.Id, ProductId = s.ProdutoId.ToString(), Name = "Lasanha", Qty = 1, UnitPrice = 30m });
                db.Add(doAparelho);
            }
            await db.SaveChangesAsync();
        }
        await using var provider = BuildProductionProvider();
        await AbastecerAsync(provider, s, 10m);
        await AbastecerAsync(provider, s with { ProdutoId = nhoqueId }, 5m);
        // O cenário depende de a lasanha baixar antes de o nhoque estourar. Os itens vêm sem ordem
        // declarada; se um dia vierem invertidos, o teste avisa em vez de passar sem exercitar nada.
        await Escopo(provider, s.EmpresaId, async sp =>
            (await sp.GetRequiredService<IPedidoRepository>().GetByIdWithDetailsAsync(s.EmpresaId, s.PedidoId))!
                .Itens.First().ProdutoId.Should().Be(s.ProdutoId, "o item que baixa tem de vir antes do que estoura"));

        await Escopo(provider, s.EmpresaId, sp => sp.GetRequiredService<OrderLinker>()
            .ExecuteAsync([s.PedidoId.ToString(), seguinteId.ToString()], s.EmpresaId));

        (await StatusDoPedido(s)).Should().Be("preparando", "o primeiro passo valeu; o segundo falhou na baixa");
        (await StatusDoPedido(s with { PedidoId = seguinteId })).Should().Be("preparando", "o pedido seguinte segue o seu caminho");
        (await Saldo(s)).Should().Be(10m, "a baixa que falhou no meio não vai ao banco com o pedido seguinte");
    }

    private Task AbastecerAsync(ServiceProvider provider, Semente s, decimal quantidade) =>
        Escopo(provider, s.EmpresaId, sp => sp.GetRequiredService<RegistrarEntradaEstoqueUseCase>().ExecuteAsync(
            new RegistrarEntradaEstoqueCommand(s.EmpresaId, s.ProdutoId, null, quantidade, 4m, null, DateTime.UtcNow.AddDays(-1),
                NaturezaMovimentacaoEstoque.Compra, null, null, null, null, null, null, null, null, null, null, null, null, null)));

    private async Task<List<string>> ObservacoesDasVendas(Semente s)
    {
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(s.EmpresaId);
        return await db.Set<Venda>().Where(v => v.EmpresaId == s.EmpresaId).Select(v => v.Observacoes ?? "").ToListAsync();
    }

    private async Task<List<decimal>> Recebimentos(Semente s)
    {
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(s.EmpresaId);
        return await db.Set<PedidoPagamento>().Where(p => p.PedidoId == s.PedidoId).Select(p => p.Valor).ToListAsync();
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
        // O pull leva o item ao aparelho com o id do produto do ERP; o espelho dele no aparelho já vinculado.
        seed.Set<Product>().Add(new Product
        {
            Id = produtoId.ToString(), Name = "Lasanha", Category = "massa", EmpresaId = empresaId, ErpProductId = produtoId
        });
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

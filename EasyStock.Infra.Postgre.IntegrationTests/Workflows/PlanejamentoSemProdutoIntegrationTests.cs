using EasyStock.Application.Common;
using EasyStock.Application.DependencyInjection;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.UseCases.Atendimento.Producao;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Sales;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Infra.Postgre.IntegrationTests.Workflows;

/// <summary>
/// #2082 A1 contra Postgres real e o DI de produção: o prato do cardápio que nunca foi produzido (sem Produto,
/// saldo 0) e já tem pedido agendado entra na sugestão, casado pelo prato do cardápio. Antes o filtro
/// <c>ProdutoId.HasValue</c> o deixava de fora e a tela não mostrava "Falta produzir".
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class PlanejamentoSemProdutoIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    [SkippableFact]
    public async Task Prato_sem_produto_com_pedido_agendado_entra_na_sugestao_e_o_sem_pedido_nao()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var empresaId = Guid.NewGuid();
        var amanha = HorarioBrasil.Hoje().AddDays(1);
        var talharim = Guid.Empty;
        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresaId);
            db.Empresas.Add(new Empresa { Id = empresaId, Nome = "Planejamento sem produto", Documento = empresaId.ToString("N")[..14],
                CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
            var vitrine = StorefrontEntity.Criar(empresaId, $"sf-{Guid.NewGuid():N}", "Casa da Baba", 0m);
            vitrine.Ativar();
            db.Storefronts.Add(vitrine);
            var prato = CardapioItem.CriarAvulso(vitrine.Id, "Talharim 500 g", 40m);
            var semPedido = CardapioItem.CriarAvulso(vitrine.Id, "Panqueca", 30m);
            db.CardapioItens.AddRange(prato, semPedido);
            talharim = prato.Id;
            foreach (var qtd in new[] { 4m, 1m })
            {
                var pedido = Pedido.Criar(empresaId);
                pedido.Status = StatusPedidoMapper.Aguardando;
                pedido.AgendadoParaEm = HorarioBrasil.InstanteUtc(amanha, new TimeOnly(12, 0));
                pedido.Itens.Add(new PedidoItem { Id = Guid.NewGuid(), PedidoId = pedido.Id, CardapioItemId = prato.Id, Nome = "Talharim 500 g",
                    Quantidade = qtd, PrecoUnitario = 40m, Subtotal = 40m * qtd, CriadoEm = DateTime.UtcNow });
                pedido.RecalcularTotal();
                db.Pedidos.Add(pedido);
            }
            await db.SaveChangesAsync();
        }
        await using var provider = BuildProductionProvider();
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<EasyStockDbContext>().SetMobileTenantContext(empresaId);

        var r = await scope.ServiceProvider.GetRequiredService<PlanejamentoDaProducaoUseCase>().SugestaoAsync(empresaId);

        var s = r.Pratos.Should().ContainSingle("só o prato com pedido agendado entra").Subject;
        s.CardapioItemId.Should().Be(talharim);
        s.ProdutoId.Should().BeNull();
        (s.Agendados, s.Saldo, s.Sugestao).Should().Be((5m, 0m, 5m));
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
        return services.BuildServiceProvider();
    }
}

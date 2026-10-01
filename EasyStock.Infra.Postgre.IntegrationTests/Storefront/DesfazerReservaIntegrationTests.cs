using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Storefront;
using EasyStock.Application.UseCases.Storefront.Frete;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Sales;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories.Storefront;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Storefront;

/// <summary>
/// #1301: quando a primeira cobrança não sai, <see cref="CheckoutCoreService.DesfazerReservaAsync"/> precisa
/// gravar no Postgres o pedido cancelado, a vaga liberada e o motivo no histórico. A liberação da vaga só marca
/// a entidade; o teste prova que o SaveChanges do pedido a leva junto.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class DesfazerReservaIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    [SkippableFact]
    public async Task MercadoPagoFora_GravaPedidoCanceladoVagaLiberadaEMotivo()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");

        var empresaId = Guid.NewGuid();
        var dataEntrega = new DateOnly(2026, 6, 1);
        Domain.Entities.Storefront.Storefront storefront;
        JanelaEntrega janela;
        Guid pedidoId;
        await using (var setup = fixture.CreateDbContext())
        {
            await setup.Database.MigrateAsync();
            setup.SetMobileTenantContext(empresaId);
            var empresa = Empresa.Criar($"Empresa {empresaId:N}", null);
            empresa.Id = empresaId;
            setup.Empresas.Add(empresa);
            storefront = Domain.Entities.Storefront.Storefront.Criar(empresaId, $"sf-{Guid.NewGuid():N}", "Loja", 0m);
            setup.Storefronts.Add(storefront);
            janela = JanelaEntrega.Criar(storefront.Id, 1, new TimeOnly(9, 0), new TimeOnly(12, 0), 1, "Única vaga");
            setup.JanelasEntrega.Add(janela);
            var pedido = Pedido.Criar(empresaId, origem: "storefront-guest");
            pedido.Status = StatusPedidoMapper.AguardandoPagamento;
            setup.Pedidos.Add(pedido);
            await setup.SaveChangesAsync();
            pedidoId = pedido.Id;
            await new VagaOcupadaRepository(setup).OcuparAsync(janela.Id, dataEntrega, pedidoId);
        }

        // Mesmo escopo da requisição: pedido rastreado no DbContext dos repositórios, como no checkout.
        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresaId);
            var pedido = await db.Pedidos.SingleAsync(p => p.Id == pedidoId);
            var reservado = new PedidoReservado(pedido, storefront, [], new PedidoItem(), 0m);

            await Nucleo(db).DesfazerReservaAsync(reservado, CheckoutCoreService.MotivoMercadoPagoIndisponivel);
        }

        await using var leitura = fixture.CreateDbContext();
        leitura.SetMobileTenantContext(empresaId);
        (await leitura.Pedidos.AsNoTracking().SingleAsync(p => p.Id == pedidoId)).Status
            .Should().Be(StatusPedidoMapper.Cancelado);
        (await leitura.VagasOcupadas.IgnoreQueryFilters().AsNoTracking().SingleAsync(v => v.PedidoId == pedidoId))
            .LiberadoEm.Should().NotBeNull("a vaga volta para a janela");
        (await leitura.Set<PedidoEvento>().AsNoTracking().SingleAsync(e => e.PedidoId == pedidoId)).Detalhes
            .Should().Be(CheckoutCoreService.MotivoMercadoPagoIndisponivel);

        // A capacidade voltou: outro pedido ocupa a única vaga da janela.
        await using var outro = fixture.CreateDbContext();
        outro.SetMobileTenantContext(empresaId);
        var segundo = Pedido.Criar(empresaId, origem: "storefront-guest");
        outro.Pedidos.Add(segundo);
        await outro.SaveChangesAsync();
        var ocupar = () => new VagaOcupadaRepository(outro).OcuparAsync(janela.Id, dataEntrega, segundo.Id);
        await ocupar.Should().NotThrowAsync();
    }

    private static CheckoutCoreService Nucleo(EasyStockDbContext db)
    {
        var storefronts = Substitute.For<IStorefrontRepository>();
        return new CheckoutCoreService(
            storefronts, Substitute.For<ICardapioItemRepository>(), Substitute.For<IJanelaEntregaRepository>(),
            Substitute.For<IBloqueioEntregaRepository>(),
            new CalcularFreteUseCase(storefronts, Substitute.For<IFreteZonaRepository>(),
                Substitute.For<Application.Ports.Output.Lookup.ICepLookupClient>(),
                Substitute.For<Application.Ports.Output.Lookup.IGeocodingClient>(),
                Substitute.For<Application.Ports.Output.Lookup.IRotaClient>(), NullLogger<CalcularFreteUseCase>.Instance),
            new VagaOcupadaRepository(db), new PedidoStorefrontRepository(db), Substitute.For<IExpedienteLojaRepository>(),
            NullLogger<CheckoutCoreService>.Instance, TimeProvider.System);
    }
}

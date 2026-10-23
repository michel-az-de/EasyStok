using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Sales;
using EasyStock.Infra.Postgre.DependencyInjection;
using EasyStock.Infra.Postgre.Hosting;
using EasyStock.Infra.Postgre.Repositories.Storefront;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Storefront;

/// <summary>
/// #1302: a varredura de pedidos abandonados cancelava o pedido e deixava a vaga ocupada, porque a
/// liberação só marca a entidade e vinha depois do único SaveChanges do escopo. A janela seguia
/// esgotada no site. Roda no DI de produção, com o login sujeito a RLS.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class CancelarPedidosAbandonadosIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    [SkippableFact]
    public async Task PedidoAbandonado_GravaCanceladoEDevolveAVagaDaJanela()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");

        var empresaId = Guid.NewGuid();
        var dataEntrega = new DateOnly(2026, 6, 1);
        JanelaEntrega janela;
        Guid pedidoId;
        await using (var setup = fixture.CreateDbContext())
        {
            await setup.Database.MigrateAsync();
            setup.SetMobileTenantContext(empresaId);
            var empresa = Empresa.Criar($"Empresa {empresaId:N}", null);
            empresa.Id = empresaId;
            setup.Empresas.Add(empresa);
            var storefront = Domain.Entities.Storefront.Storefront.Criar(empresaId, $"sf-{Guid.NewGuid():N}", "Loja", 0m);
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

        await using (var provider = BuildProvider())
        {
            var varredura = new CancelarPedidosAbandonadosBackgroundService(
                provider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<CancelarPedidosAbandonadosBackgroundService>.Instance);

            await varredura.CancelarAbandonadoAsync(pedidoId, empresaId);
        }

        await using var leitura = fixture.CreateDbContext();
        leitura.SetMobileTenantContext(empresaId);
        (await leitura.Pedidos.AsNoTracking().SingleAsync(p => p.Id == pedidoId)).Status
            .Should().Be(StatusPedidoMapper.Cancelado);
        (await leitura.VagasOcupadas.IgnoreQueryFilters().AsNoTracking().SingleAsync(v => v.PedidoId == pedidoId))
            .LiberadoEm.Should().NotBeNull("a vaga volta para a janela junto com o cancelamento");
        // #1726: o cancelamento automático não é mais silencioso.
        (await leitura.Set<PedidoEvento>().IgnoreQueryFilters().AsNoTracking().SingleAsync(e => e.PedidoId == pedidoId))
            .Should().Match<PedidoEvento>(e => e.Tipo == "cancelado" && e.Origem == "sistema"
                && e.Detalhes == CancelarPedidosAbandonadosBackgroundService.MotivoAbandonado);

        // A capacidade voltou: outro pedido ocupa a única vaga da janela.
        var segundo = Pedido.Criar(empresaId, origem: "storefront-guest");
        leitura.Pedidos.Add(segundo);
        await leitura.SaveChangesAsync();
        var ocupar = () => new VagaOcupadaRepository(leitura).OcuparAsync(janela.Id, dataEntrega, segundo.Id);
        await ocupar.Should().NotThrowAsync();
    }

    /// <summary>
    /// #1623-a: checkout do site interrompido na fase 1 deixa o pedido em rascunho, sem cobrança, para sempre. A rodada
    /// cancela o rascunho antigo do checkout com a trilha de sempre e deixa o recente (checkout em andamento).
    /// </summary>
    [SkippableFact]
    public async Task RascunhoAntigoDoCheckout_ARodadaCancelaEDeixaORecente()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");

        var empresaId = Guid.NewGuid();
        Guid antigoId, recenteId;
        await using (var setup = fixture.CreateDbContext())
        {
            await setup.Database.MigrateAsync();
            setup.SetMobileTenantContext(empresaId);
            var empresa = Empresa.Criar($"Empresa {empresaId:N}", null);
            empresa.Id = empresaId;
            setup.Empresas.Add(empresa);
            var antigo = Pedido.Criar(empresaId, origem: "storefront");
            antigo.Status = StatusPedidoMapper.Rascunho;
            antigo.CriadoEm = DateTime.UtcNow.AddHours(-2);
            var recente = Pedido.Criar(empresaId, origem: "storefront");
            recente.Status = StatusPedidoMapper.Rascunho;
            setup.Pedidos.AddRange(antigo, recente);
            await setup.SaveChangesAsync();
            (antigoId, recenteId) = (antigo.Id, recente.Id);
        }

        await using (var provider = BuildProvider())
            await new CancelarPedidosAbandonadosBackgroundService(provider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<CancelarPedidosAbandonadosBackgroundService>.Instance).RodadaAsync(CancellationToken.None);

        await using var leitura = fixture.CreateDbContext();
        leitura.SetMobileTenantContext(empresaId);
        (await leitura.Pedidos.AsNoTracking().SingleAsync(p => p.Id == antigoId)).Status.Should().Be(StatusPedidoMapper.Cancelado);
        (await leitura.Pedidos.AsNoTracking().SingleAsync(p => p.Id == recenteId)).Status.Should().Be(StatusPedidoMapper.Rascunho);
        (await leitura.Set<PedidoEvento>().IgnoreQueryFilters().AsNoTracking().SingleAsync(e => e.PedidoId == antigoId))
            .Should().Match<PedidoEvento>(e => e.Tipo == "cancelado" && e.StatusAntigo == StatusPedidoMapper.Rascunho
                && e.Detalhes == CancelarPedidosAbandonadosBackgroundService.MotivoAbandonado);
    }

    private ServiceProvider BuildProvider()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection([]).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddMemoryCache();
        services.AddHttpContextAccessor();
        services.AddSingleton(Substitute.For<ICurrentUserAccessor>()); // sem JWT, como no job
        services.AddSingleton(Substitute.For<ICacheService>());
        services.AddEasyStockPostgreInfrastructure(fixture.RlsClientConnectionString, config);
        return services.BuildServiceProvider();
    }
}

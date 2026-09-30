using EasyStock.Application.DependencyInjection;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.UseCases.CriarPedido;
using EasyStock.Application.UseCases.RegistrarPagamentoPedido;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.DependencyInjection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Workflows;

/// <summary>
/// #1230 (pendência da S21): pedido que entra na fila fora do Mercado Pago grava o início previsto no Postgres
/// real, com os use cases resolvidos do DI de produção. Criação no ERP grava num segundo commit (a leitura
/// exige o pedido no banco); pagamento manual grava junto com o pagamento.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class InicioPrevistoNaFilaIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    [SkippableFact]
    public async Task PedidoAgendadoCriadoNoErp_PersisteInicioPrevisto()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var empresaId = await SemearEmpresaAsync();
        var entrega = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(3).AddHours(15), DateTimeKind.Utc);

        await using var provider = BuildProductionProvider();
        Guid pedidoId;
        await using (var scope = provider.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<EasyStockDbContext>().SetMobileTenantContext(empresaId);
            var criado = await scope.ServiceProvider.GetRequiredService<CriarPedidoUseCase>().ExecuteAsync(
                new CriarPedidoCommand(empresaId, ClienteNomeAdHoc: "Cliente S21",
                    Itens: [new CriarPedidoItemInput("Bolo", 1, 50m)], AgendadoParaEm: entrega));
            pedidoId = criado.Id;
        }

        (await LerInicioPrevistoAsync(empresaId, pedidoId)).Should().Be(entrega.AddMinutes(-PrazoPadrao(empresaId)));
    }

    [SkippableFact]
    public async Task PagamentoManualEmPedidoDaFila_PersisteInicioPrevisto()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var empresaId = await SemearEmpresaAsync();
        var entrega = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(3).AddHours(15), DateTimeKind.Utc);

        // Pedido que já estava na fila sem início previsto (criado antes do #1230).
        var pedido = Pedido.Criar(empresaId);
        pedido.AgendadoParaEm = entrega;
        var item = new PedidoItem
        {
            Id = Guid.NewGuid(), PedidoId = pedido.Id, Nome = "Bolo", Quantidade = 1,
            PrecoUnitario = 50m, Subtotal = 50m, CriadoEm = DateTime.UtcNow,
        };
        pedido.Itens.Add(item);
        pedido.RecalcularTotal();
        await using (var seed = fixture.CreateDbContext())
        {
            seed.SetMobileTenantContext(empresaId);
            seed.Pedidos.Add(pedido);
            await seed.SaveChangesAsync();
        }

        await using var provider = BuildProductionProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<EasyStockDbContext>().SetMobileTenantContext(empresaId);
            await scope.ServiceProvider.GetRequiredService<RegistrarPagamentoPedidoUseCase>().ExecuteAsync(
                new RegistrarPagamentoPedidoCommand(empresaId, pedido.Id, "pix", 50m, RegistradoPorNome: "Dona"));
        }

        (await LerInicioPrevistoAsync(empresaId, pedido.Id)).Should().Be(entrega.AddMinutes(-PrazoPadrao(empresaId)));
    }

    /// <summary>Sem linha de configuração e sem item do cardápio vale o padrão da empresa mais o respiro.</summary>
    private static int PrazoPadrao(Guid empresaId)
    {
        var padrao = ConfiguracaoAtendimento.CriarPadrao(empresaId);
        return padrao.TempoPreparoPadraoMinutos + padrao.RespiroMinutos;
    }

    private async Task<Guid> SemearEmpresaAsync()
    {
        var empresaId = Guid.NewGuid();
        await using var seed = fixture.CreateDbContext();
        seed.SetMobileTenantContext(empresaId);
        seed.Empresas.Add(new Empresa
        {
            Id = empresaId,
            Nome = "Empresa #1230",
            Documento = empresaId.ToString("N")[..14],
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow,
        });
        await seed.SaveChangesAsync();
        return empresaId;
    }

    private async Task<DateTime?> LerInicioPrevistoAsync(Guid empresaId, Guid pedidoId)
    {
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresaId);
        return await db.Pedidos.AsNoTracking().Where(p => p.Id == pedidoId).Select(p => p.InicioPrevistoEm).SingleAsync();
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

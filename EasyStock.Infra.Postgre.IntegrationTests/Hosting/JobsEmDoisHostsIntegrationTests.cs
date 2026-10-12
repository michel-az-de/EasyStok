using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Sales;
using EasyStock.Infra.Postgre.Concurrency;
using EasyStock.Infra.Postgre.DependencyInjection;
using EasyStock.Infra.Postgre.Hosting;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Hosting;

/// <summary>
/// #1575: API e Worker registram os três serviços de <c>AddEasyStockPostgreInfrastructure</c>. Com a trava
/// detida por outra sessão (o outro host no meio da rodada), a rodada daqui pula sem fazer nada e sem
/// erro; com a trava livre, roda como antes. DI de produção.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class JobsEmDoisHostsIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    [SkippableFact]
    public async Task PedidosAbandonados_ComATravaDoOutroHost_PulaARodada()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var empresa = Empresa.Criar("Jobs em dois hosts", null);
        var pedido = Pedido.Criar(empresa.Id, origem: "storefront-guest");
        pedido.Status = StatusPedidoMapper.AguardandoPagamento;
        // Bem antigo: entra no lote de 50 antes de qualquer sobra de outro teste da coleção.
        pedido.CriadoEm = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await using (var seed = fixture.CreateDbContext())
        {
            seed.SetMobileTenantContext(empresa.Id);
            seed.Empresas.Add(empresa);
            seed.Pedidos.Add(pedido);
            await seed.SaveChangesAsync();
        }
        var logger = Substitute.For<ILogger<CancelarPedidosAbandonadosBackgroundService>>();
        await using var provider = BuildProvider(fixture.RlsClientConnectionString);
        var varredura = new CancelarPedidosAbandonadosBackgroundService(provider.GetRequiredService<IServiceScopeFactory>(), logger);

        await using (await DeterTravaAsync(LockKeys.PedidosAbandonados))
            await varredura.RodadaAsync(CancellationToken.None);

        (await StatusAsync(empresa.Id, pedido.Id)).Should().Be(StatusPedidoMapper.AguardandoPagamento, "o outro host está na rodada");
        logger.ReceivedCalls().Should().BeEmpty("pular a rodada não é erro");

        await varredura.RodadaAsync(CancellationToken.None);

        (await StatusAsync(empresa.Id, pedido.Id)).Should().Be(StatusPedidoMapper.Cancelado, "com a trava livre a rodada roda como antes");
    }

    [SkippableFact]
    public async Task RetencaoDeAuditoria_ComATravaDoOutroHost_PulaARodada()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var empresa = Empresa.Criar("Retenção em dois hosts", null);
        var antiga = new EntityAlteracao
        {
            Id = Guid.NewGuid(), EmpresaId = empresa.Id, TipoEntidade = "Teste1575", EntidadeId = Guid.NewGuid(),
            Acao = "Update", AlteradoEm = DateTime.UtcNow.AddDays(-400),
        };
        await using (var seed = fixture.CreateDbContext())
        {
            seed.SetMobileTenantContext(empresa.Id);
            seed.Empresas.Add(empresa);
            seed.Set<EntityAlteracao>().Add(antiga);
            await seed.SaveChangesAsync();
        }
        var logger = Substitute.For<ILogger<EntityAlteracaoRetentionService>>();
        // Superuser: a prova é da trava, não do RLS da tabela.
        await using var provider = BuildProvider(fixture.ConnectionString);
        var retencao = new EntityAlteracaoRetentionService(provider, logger);

        await using (await DeterTravaAsync(LockKeys.RetencaoAuditoria))
            await retencao.RodadaAsync(CancellationToken.None);

        (await ExisteAsync(antiga.Id)).Should().BeTrue("o outro host está na rodada");
        logger.ReceivedCalls().Should().BeEmpty("pular a rodada não é erro");

        await retencao.RodadaAsync(CancellationToken.None);

        (await ExisteAsync(antiga.Id)).Should().BeFalse("com a trava livre a rodada roda como antes");
    }

    [SkippableFact]
    public async Task AlertaDeAparelho_ComATravaDoOutroHost_PulaARodada()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var logger = Substitute.For<ILogger<MobileAlertService>>();
        await using var provider = BuildProvider(fixture.ConnectionString);
        var alerta = new MobileAlertService(provider.GetRequiredService<IServiceScopeFactory>(), logger);

        await using (await DeterTravaAsync(LockKeys.MobileAlerta))
            await alerta.RodadaAsync(CancellationToken.None);

        logger.ReceivedCalls().Should().BeEmpty("com a trava do outro host a rodada nem consulta nem avisa");

        await alerta.RodadaAsync(CancellationToken.None);

        logger.ReceivedCalls().Should().NotBeEmpty("com a trava livre a rodada roda e registra o resultado");
    }

    /// <summary>
    /// Outra sessão segurando a trava, como o outro host no meio da rodada. Sem pool: fechar a conexão
    /// encerra a sessão e solta a trava (no pool ela ficaria presa na conexão ociosa).
    /// </summary>
    private async Task<NpgsqlConnection> DeterTravaAsync(long chave)
    {
        var conexao = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Pooling = false }.ConnectionString);
        await conexao.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT pg_advisory_lock(@k)", conexao);
        cmd.Parameters.AddWithValue("k", chave);
        await cmd.ExecuteNonQueryAsync();
        return conexao;
    }

    private async Task<string> StatusAsync(Guid empresaId, Guid pedidoId)
    {
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresaId);
        return (await db.Pedidos.AsNoTracking().SingleAsync(p => p.Id == pedidoId)).Status;
    }

    private async Task<bool> ExisteAsync(Guid alteracaoId)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Set<EntityAlteracao>().IgnoreQueryFilters().AnyAsync(a => a.Id == alteracaoId);
    }

    private static ServiceProvider BuildProvider(string connectionString)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection([]).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddMemoryCache();
        services.AddHttpContextAccessor();
        services.AddSingleton(Substitute.For<ICurrentUserAccessor>()); // sem JWT, como no job
        services.AddSingleton(Substitute.For<ICacheService>());
        services.AddEasyStockPostgreInfrastructure(connectionString, config);
        return services.BuildServiceProvider();
    }
}

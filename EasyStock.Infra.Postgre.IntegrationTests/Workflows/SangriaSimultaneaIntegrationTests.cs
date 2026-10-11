using EasyStock.Application.DependencyInjection;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.RegistrarMovimentoCaixa;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.DependencyInjection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Workflows;

/// <summary>
/// #1547: duas sangrias simultâneas liam o mesmo saldo da gaveta e as duas passavam do teto.
/// Contra Postgres real, com o DI de produção e um <see cref="EasyStockDbContext"/> por requisição:
/// gaveta de R$ 100, oito saídas de R$ 80 em dinheiro ao mesmo tempo. Só uma cabe.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class SangriaSimultaneaIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    [SkippableFact]
    public async Task Sangrias_simultaneas_nao_passam_do_dinheiro_na_gaveta()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var empresaId = Guid.NewGuid();
        var loja = Loja.Criar(empresaId, "Loja sangria");
        await using (var seed = fixture.CreateDbContext())
        {
            seed.Empresas.Add(new Empresa { Id = empresaId, Nome = "Sangria simultânea", Documento = Guid.NewGuid().ToString("N")[..14],
                CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
            seed.Lojas.Add(loja);
            seed.SetMobileTenantContext(empresaId);
            seed.MovimentosCaixa.Add(MovimentoCaixa.Criar(empresaId, "abertura", 100m, DateTime.UtcNow.AddMinutes(-5), loja.Id));
            await seed.SaveChangesAsync();
        }
        await using var provider = BuildProductionProvider();
        var largada = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> Sangrar()
        {
            await using var scope = provider.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<EasyStockDbContext>().SetMobileTenantContext(empresaId);
            var useCase = scope.ServiceProvider.GetRequiredService<RegistrarMovimentoCaixaUseCase>();
            await largada.Task;
            try
            {
                await useCase.ExecuteAsync(new RegistrarMovimentoCaixaCommand(empresaId, "saida", 80m,
                    Descricao: "Sangria", LojaId: loja.Id, Metodo: "dinheiro"));
                return true;
            }
            catch (UseCaseValidationException ex) when (ex.Message.Contains("gaveta"))
            {
                return false;
            }
        }

        var corridas = Enumerable.Range(0, 8).Select(_ => Task.Run(Sangrar)).ToArray();
        largada.SetResult();
        var aceitas = (await Task.WhenAll(corridas)).Count(ok => ok);

        aceitas.Should().Be(1, "a gaveta tem R$ 100 e cada sangria leva R$ 80");
        await using var conferir = fixture.CreateDbContext();
        conferir.SetMobileTenantContext(empresaId);
        (await conferir.MovimentosCaixa.Where(m => m.Tipo == "saida").SumAsync(m => m.Valor)).Should().Be(80m);
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

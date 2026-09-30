using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Async;
using EasyStock.Infra.Postgre.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Atendimento;

/// <summary>
/// #1102: o número pelo qual a resposta sai é o da empresa do tenant corrente. Resolvido via DI
/// real e com a role sujeita a RLS, porque é assim que o webhook e os jobs rodam (sem JWT, tenant
/// fixado por <see cref="ITenantContextAccessor"/>).
/// </summary>
public class RemetenteWhatsAppDoTenantIntegrationTests(PostgreSqlDatabaseFixture fixture) : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task DevolveONumeroDaEmpresaDoTenantCorrente()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        await fixture.ResetDatabaseAsync();

        var empresaX = Empresa.Criar("Empresa X", "11111111000191");
        empresaX.VincularWhatsApp("5550001111");
        var empresaY = Empresa.Criar("Empresa Y", "22222222000191");
        empresaY.VincularWhatsApp("5550002222");
        var semNumero = Empresa.Criar("Sem numero", "33333333000191");
        await SeedAsync(empresaX, empresaY, semNumero);

        await using var provider = BuildProvider();

        (await ResolverAsync(provider, empresaX.Id)).Should().Be("5550001111");
        (await ResolverAsync(provider, empresaY.Id)).Should().Be("5550002222");
        (await ResolverAsync(provider, semNumero.Id)).Should().BeNull("sem número vinculado o cliente cai no global");
        (await ResolverAsync(provider, tenant: null)).Should().BeNull("sem tenant não há empresa para escolher");
    }

    private async Task SeedAsync(params Empresa[] empresas)
    {
        await using var seed = fixture.CreateDbContext();
        seed.Set<Empresa>().AddRange(empresas);
        await seed.SaveChangesAsync();
    }

    private ServiceProvider BuildProvider()
    {
        var config = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddMemoryCache();
        services.AddHttpContextAccessor();
        services.AddSingleton(Substitute.For<ICurrentUserAccessor>()); // sem JWT: IsAuthenticated=false
        services.AddSingleton(Substitute.For<ICacheService>());
        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.AddEasyStockPostgreInfrastructure(fixture.RlsClientConnectionString, config);
        return services.BuildServiceProvider();
    }

    private static async Task<string?> ResolverAsync(ServiceProvider provider, Guid? tenant)
    {
        await using var scope = provider.CreateAsyncScope();
        if (tenant is { } id)
            scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetCurrentTenant(id);
        return await scope.ServiceProvider.GetRequiredService<IRemetenteWhatsApp>().ObterPhoneNumberIdAsync();
    }
}

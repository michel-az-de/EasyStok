using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Integration.Crypto;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Integration;
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

    [SkippableFact]
    public async Task DevolveOBusinessTokenDaEmpresaQuandoHaCredencial()
    {
        // #1417: com credencial meta-whatsapp o envio usa o token da empresa; sem ela, null (o cliente cai no global).
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        await fixture.ResetDatabaseAsync();

        var conectada = Empresa.Criar("Conectada", "11111111000191");
        var semCredencial = Empresa.Criar("Sem credencial", "22222222000191");
        await SeedAsync(conectada, semCredencial);

        await using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Crypto:CurrentKekId"] = "kek-teste",
            ["Crypto:Keks:kek-teste"] = Convert.ToBase64String(new byte[32]),
        });

        await using (var scope = provider.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetCurrentTenant(conectada.Id);
            await scope.ServiceProvider.GetRequiredService<IIntegrationCredentialResolver>().SalvarAsync(
                conectada.Id, CategoriaIntegracao.Mensageria, CredencialWhatsAppMeta.ProviderKey, AmbienteIntegracao.Production,
                new CredencialWhatsAppMeta("token-da-conectada", "1001", "5550001111", DateTime.UtcNow), Guid.NewGuid());
        }

        (await TokenAsync(provider, conectada.Id)).Should().Be("token-da-conectada");
        (await TokenAsync(provider, semCredencial.Id)).Should().BeNull("sem credencial vale o token global");
        (await TokenAsync(provider, tenant: null)).Should().BeNull();
    }

    private static async Task<string?> TokenAsync(ServiceProvider provider, Guid? tenant)
    {
        await using var scope = provider.CreateAsyncScope();
        if (tenant is { } id)
            scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetCurrentTenant(id);
        return await scope.ServiceProvider.GetRequiredService<IRemetenteWhatsApp>().ObterAccessTokenAsync();
    }

    private async Task SeedAsync(params Empresa[] empresas)
    {
        await using var seed = fixture.CreateDbContext();
        seed.Set<Empresa>().AddRange(empresas);
        await seed.SaveChangesAsync();
    }

    private ServiceProvider BuildProvider(Dictionary<string, string?>? valores = null)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(valores ?? []).Build();
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

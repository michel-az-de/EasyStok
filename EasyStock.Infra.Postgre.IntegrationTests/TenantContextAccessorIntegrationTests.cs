using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Infra.Async;
using EasyStock.Infra.Postgre.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests;

/// <summary>
/// Prova que <see cref="ITenantContextAccessor"/> (S03) — resolvido via DI real, não instanciado
/// na mão — realmente liga o filtro global de tenant e a RLS (mesmo mecanismo de
/// <c>SetMobileTenantContext</c>, já provado por <see cref="Repositories.ConversaRepositoryIntegrationTests"/>)
/// para o fluxo do webhook, que não tem claim JWT nenhum.
/// </summary>
public class TenantContextAccessorIntegrationTests(PostgreSqlDatabaseFixture fixture) : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task ResolveEmpresaPorPhoneNumberIdEEscreveConversaComTenantCerto()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        await fixture.ResetDatabaseAsync();

        var empresa = Empresa.Criar("Casa da Baba", "11111111000191");
        empresa.VincularWhatsApp("551199990000");
        await using (var seed = fixture.CreateDbContext())
        {
            seed.Set<Empresa>().Add(empresa);
            await seed.SaveChangesAsync();
        }

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
        await using var provider = services.BuildServiceProvider();

        await using var scope = provider.CreateAsyncScope();
        var empresaRepository = scope.ServiceProvider.GetRequiredService<IEmpresaRepository>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
        var conversaRepository = scope.ServiceProvider.GetRequiredService<IConversaRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var resolvida = await empresaRepository.GetByWhatsAppPhoneNumberIdAsync("551199990000");
        resolvida.Should().NotBeNull();
        resolvida!.Id.Should().Be(empresa.Id);

        tenantContext.SetCurrentTenant(resolvida.Id);

        var conversa = Conversa.Abrir(resolvida.Id, "5511988887777", DateTime.UtcNow);
        await conversaRepository.AddAsync(conversa);
        await unitOfWork.CommitAsync();

        var lida = await conversaRepository.ObterPorIdAsync(resolvida.Id, conversa.Id);
        lida.Should().NotBeNull("com o tenant setado via ITenantContextAccessor, o filtro global e a RLS liberam a leitura");
    }
}

using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Atendimento.Email;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Infra.Async;
using EasyStock.Infra.Postgre.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Atendimento;

/// <summary>
/// #1642 (item 6): a automática de encerramento sai com a conversa JÁ encerrada. O fio precisa achar essa conversa,
/// senão o e-mail sai com assunto padrão e sem In-Reply-To, fora da conversa que o cliente vê.
/// </summary>
public class CaixaEmailDoTenantFioIntegrationTests(PostgreSqlDatabaseFixture fixture) : IClassFixture<PostgreSqlDatabaseFixture>
{
    private const string Contato = "cliente@exemplo.com";

    [SkippableFact]
    public async Task Conversa_recem_encerrada_ainda_da_o_fio_da_mensagem_de_encerramento()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        await fixture.ResetDatabaseAsync();

        var empresa = Empresa.Criar("Loja", "11111111000191");
        var agora = DateTime.UtcNow;
        var encerrada = ConversaDeEmail(empresa.Id, agora.AddMinutes(-10), "Pedido 42", "<pedido-42@cliente>");
        encerrada.conversa.Encerrar(agora);
        await SeedAsync(empresa, encerrada);

        var fio = await ObterFioAsync(empresa.Id);

        fio.Should().Be(new FioEmail("Pedido 42", "<pedido-42@cliente>"));
    }

    [SkippableFact]
    public async Task Conversa_aberta_vence_a_encerrada_mais_recente()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        await fixture.ResetDatabaseAsync();

        var empresa = Empresa.Criar("Loja", "11111111000191");
        var agora = DateTime.UtcNow;
        var aberta = ConversaDeEmail(empresa.Id, agora.AddHours(-2), "Troca", "<troca@cliente>");
        var encerrada = ConversaDeEmail(empresa.Id, agora.AddMinutes(-5), "Pedido 42", "<pedido-42@cliente>");
        encerrada.conversa.Encerrar(agora);
        await SeedAsync(empresa, aberta, encerrada);

        var fio = await ObterFioAsync(empresa.Id);

        fio.Should().Be(new FioEmail("Troca", "<troca@cliente>"));
    }

    private static (Conversa conversa, Mensagem mensagem) ConversaDeEmail(Guid empresaId, DateTime em, string assunto, string messageId)
    {
        var conversa = Conversa.Abrir(empresaId, Contato, em, canal: CanalConversa.Email);
        conversa.DefinirAssunto(assunto);
        var mensagem = Mensagem.Entrada(empresaId, conversa.Id, em, TipoConteudoMensagem.Texto, "olá", messageId);
        return (conversa, mensagem);
    }

    private async Task SeedAsync(Empresa empresa, params (Conversa conversa, Mensagem mensagem)[] conversas)
    {
        await using var seed = fixture.CreateDbContext();
        seed.Set<Empresa>().Add(empresa);
        foreach (var (conversa, mensagem) in conversas)
        {
            seed.Set<Conversa>().Add(conversa);
            seed.Set<Mensagem>().Add(mensagem);
        }
        await seed.SaveChangesAsync();
    }

    private async Task<FioEmail?> ObterFioAsync(Guid empresaId)
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetCurrentTenant(empresaId);
        return await scope.ServiceProvider.GetRequiredService<ICaixaEmailDoTenant>().ObterFioAsync(Contato);
    }

    private ServiceProvider BuildProvider()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection([]).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddMemoryCache();
        services.AddHttpContextAccessor();
        services.AddSingleton(Substitute.For<ICurrentUserAccessor>());
        services.AddSingleton(Substitute.For<ICacheService>());
        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.AddEasyStockPostgreInfrastructure(fixture.RlsClientConnectionString, config);
        return services.BuildServiceProvider();
    }
}

using EasyStock.Application.DependencyInjection;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Services.Ia;
using EasyStock.Domain.Enums.Ia;
using EasyStock.Infra.Postgre.DependencyInjection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Ia;

/// <summary>
/// #3063 (S2a de #2948): com o login sujeito a RLS (o mesmo papel da produção), N chamadas ao LLM viram N linhas de
/// chamada_ia na loja certa, e a soma do mês enxerga só a loja pedida.
/// </summary>
public class RegistroChamadaIaIntegrationTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 21, 15, 0, 0, TimeSpan.Zero);

    private ServiceProvider Provedor()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection([]).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddMemoryCache();
        services.AddHttpContextAccessor();
        services.AddSingleton(Substitute.For<ICurrentUserAccessor>());
        services.AddSingleton(Substitute.For<ICacheService>());
        services.AddEasyStockPostgreInfrastructure(fixture.RlsClientConnectionString, config);
        services.AddEasyStockApplication();
        return services.BuildServiceProvider();
    }

    private static RegistroChamadaIaLlmClient Cliente(IServiceProvider sp)
    {
        var interno = Substitute.For<IAgenteLlmClient>();
        interno.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>())
            .Returns(new RespostaLlm("end_turn", [], 100, 20, 0, "deepseek-flash"));
        return new RegistroChamadaIaLlmClient(interno, sp.GetRequiredService<IRegistroChamadaIa>(), new RelogioFixo(),
            new LogEmLista());
    }

    private static RequisicaoLlm Requisicao(Guid empresa, FinalidadeIa finalidade) =>
        new("s", [new MensagemLlm(MensagemLlm.Usuario, [new BlocoTextoLlm("oi")])], [])
        { EmpresaId = empresa, Finalidade = finalidade };

    [SkippableFact]
    public async Task NChamadasGeramNLinhasNaLojaCertaPeloLoginComRls()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresaA = Guid.NewGuid();
        var empresaB = Guid.NewGuid();
        await using var sp = Provedor();
        var cliente = Cliente(sp);

        for (var i = 0; i < 3; i++) await cliente.EnviarAsync(Requisicao(empresaA, FinalidadeIa.ResumoCliente));
        await cliente.EnviarAsync(Requisicao(empresaB, FinalidadeIa.Atendimento));

        await using var db = fixture.CreateDbContext();
        (await db.ChamadasIa.IgnoreQueryFilters().Where(c => c.EmpresaId == empresaA).Select(c => c.Finalidade).ToListAsync())
            .Should().HaveCount(3, "registro engoliu: {0}", string.Join(" | ", LogEmLista.Erros)).And.OnlyContain(f => f == FinalidadeIa.ResumoCliente);
        (await db.ChamadasIa.IgnoreQueryFilters().CountAsync(c => c.EmpresaId == empresaB)).Should().Be(1);

        var uso = await sp.GetRequiredService<IRegistroChamadaIa>().SomarMesAsync(empresaA, Agora.UtcDateTime);
        uso.Should().Be(new UsoMensalIa(3, 300, 60, 0m));
    }

    private sealed class RelogioFixo : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Agora;
    }

    private sealed class LogEmLista : Microsoft.Extensions.Logging.ILogger<RegistroChamadaIaLlmClient>
    {
        public static readonly System.Collections.Concurrent.ConcurrentQueue<string> Erros = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) =>
            Erros.Enqueue(formatter(state, exception) + " " + exception?.GetType().Name + ": " + exception?.Message);
    }
}

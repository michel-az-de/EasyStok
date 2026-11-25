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

    private static RegistroChamadaIaLlmClient Cliente(IServiceProvider sp, LogEmLista log)
    {
        var interno = Substitute.For<IAgenteLlmClient>();
        interno.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>())
            .Returns(new RespostaLlm("end_turn", [], 100, 20, 0, "deepseek-flash"));
        return new RegistroChamadaIaLlmClient(interno, sp.GetRequiredService<IRegistroChamadaIa>(), new RelogioFixo(),
            log);
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
        var log = new LogEmLista();
        var cliente = Cliente(sp, log);

        for (var i = 0; i < 3; i++) await cliente.EnviarAsync(Requisicao(empresaA, FinalidadeIa.ResumoCliente));
        await cliente.EnviarAsync(Requisicao(empresaB, FinalidadeIa.Atendimento));

        await using var db = fixture.CreateDbContext();
        (await db.ChamadasIa.IgnoreQueryFilters().Where(c => c.EmpresaId == empresaA).Select(c => c.Finalidade).ToListAsync())
            .Should().HaveCount(3, "registro engoliu: {0}", string.Join(" | ", log.Erros)).And.OnlyContain(f => f == FinalidadeIa.ResumoCliente);
        (await db.ChamadasIa.IgnoreQueryFilters().CountAsync(c => c.EmpresaId == empresaB)).Should().Be(1);

        var uso = await sp.GetRequiredService<IRegistroChamadaIa>().SomarMesAsync(empresaA, Agora.UtcDateTime);
        uso.Should().Be(new UsoMensalIa(3, 300, 60, 0m));
    }

    [SkippableFact]
    public async Task SomarMesSegueOMesDeBrasiliaNasTresUltimasHorasDoMes()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        // #3295 (R6): o mês da soma é o de Brasília, o mesmo da cota de tokens. 01/12 01:30 UTC ainda é 30/11 22:30 lá.
        var empresa = Guid.NewGuid();
        await using var sp = Provedor();
        var registro = sp.GetRequiredService<IRegistroChamadaIa>();
        foreach (var instante in new[]
                 {
                     new DateTime(2026, 11, 30, 12, 0, 0, DateTimeKind.Utc),
                     new DateTime(2026, 12, 1, 1, 30, 0, DateTimeKind.Utc),
                     new DateTime(2026, 12, 1, 3, 30, 0, DateTimeKind.Utc),
                 })
            await registro.RegistrarAsync(EasyStock.Domain.Entities.Ia.ChamadaIa.Registrar(empresa, null, null, "deepseek-flash",
                FinalidadeIa.Atendimento, null, 100, 20, 0, 0, 1m, 10, DesfechoChamadaIa.Ok, instante));

        // 22:45 de 30/11 em Brasília (01:45 UTC de 01/12): ainda novembro, soma a primeira e a segunda.
        (await registro.SomarMesAsync(empresa, new DateTime(2026, 12, 1, 1, 45, 0, DateTimeKind.Utc)))
            .Should().Be(new UsoMensalIa(2, 200, 40, 2m));
        // 00:45 de 01/12 em Brasília (03:45 UTC): dezembro, só a terceira.
        (await registro.SomarMesAsync(empresa, new DateTime(2026, 12, 1, 3, 45, 0, DateTimeKind.Utc)))
            .Should().Be(new UsoMensalIa(1, 100, 20, 1m));
    }

    private sealed class RelogioFixo : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Agora;
    }

    private sealed class LogEmLista : Microsoft.Extensions.Logging.ILogger<RegistroChamadaIaLlmClient>
    {
        public System.Collections.Concurrent.ConcurrentQueue<string> Erros { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) =>
            Erros.Enqueue(formatter(state, exception) + " " + exception?.GetType().Name + ": " + exception?.Message);
    }
}

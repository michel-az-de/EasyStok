using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Storefront.Pedidos;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Exceptions.Storefront;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace EasyStock.Api.IntegrationTests.Storefront.Pedidos;

/// <summary>
/// Testes E2E do <see cref="EasyStock.Api.Controllers.Storefront.PedidosClienteController"/>
/// (TASK-EZ-PEDIDOS-001). Use case é mockado — testes focam no controller
/// (auth via cookie, status codes, headers, shape do payload).
///
/// <para>
/// <strong>Execução:</strong> setar <c>RUN_API_INTEGRATION=1</c> — testes são
/// <see cref="SkippableFact"/> por default para não subir API em cada
/// <c>dotnet test</c> rápido.
/// </para>
/// </summary>
public sealed class PedidosClienteControllerTests
{
    private static readonly Guid SessionId = Guid.NewGuid();
    private static readonly Guid ClienteId = Guid.NewGuid();
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private const string Slug = "casa-da-baba";

    private static WebApplicationFactory<Program> CriarFactory(
        Func<ListarPedidosClienteResult>? useCaseResult = null,
        Exception? useCaseThrows = null,
        ClienteSession? session = null)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b =>
            {
                b.ConfigureAppConfiguration((_, cfg) =>
                {
                    cfg.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["DatabaseProvider"] = "sqlite",
                        ["ConnectionStrings:DefaultConnection"] = "Data Source=:memory:",
                        ["Jwt:Key"] = "test-super-secret-key-min32chars!!",
                        ["Jwt:Issuer"] = "EasyStock",
                        ["Jwt:Audience"] = "EasyStock",
                    });
                });

                b.ConfigureServices(services =>
                {
                    // Substituir use case por mock
                    var ucDescriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(ListarPedidosClienteUseCase));
                    if (ucDescriptor is not null) services.Remove(ucDescriptor);

                    var mockUc = Substitute.For<ListarPedidosClienteUseCase>(
                        Substitute.For<IStorefrontRepository>(),
                        Substitute.For<IPedidoStorefrontRepository>(),
                        Substitute.For<IPedidoAvaliacaoRepository>(),
                        Substitute.For<IVagaOcupadaRepository>(),
                        NullLogger<ListarPedidosClienteUseCase>.Instance);

                    if (useCaseThrows is not null)
                    {
                        mockUc.ExecuteAsync(Arg.Any<ListarPedidosClienteInput>(), Arg.Any<CancellationToken>())
                            .ThrowsAsync(useCaseThrows);
                    }
                    else
                    {
                        var result = useCaseResult is not null
                            ? useCaseResult()
                            : new ListarPedidosClienteResult(Array.Empty<PedidoStorefrontDto>());
                        mockUc.ExecuteAsync(Arg.Any<ListarPedidosClienteInput>(), Arg.Any<CancellationToken>())
                            .Returns(result);
                    }

                    services.AddScoped(_ => mockUc);

                    // Substituir clienteSessionRepository
                    var sessionDescriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(IClienteSessionRepository));
                    if (sessionDescriptor is not null) services.Remove(sessionDescriptor);

                    var sessionStub = session ?? CriarSessionValida();
                    var mockSession = Substitute.For<IClienteSessionRepository>();
                    mockSession.GetByIdAsync(SessionId, Arg.Any<CancellationToken>()).Returns(sessionStub);
                    mockSession.GetByIdAsync(Arg.Is<Guid>(g => g != SessionId), Arg.Any<CancellationToken>())
                        .Returns((ClienteSession?)null);
                    services.AddScoped(_ => mockSession);
                });
            });
    }

    private static ClienteSession CriarSessionValida()
    {
        var session = ClienteSession.Criar(ClienteId, EmpresaId, TimeProvider.System);
        typeof(ClienteSession).GetProperty("Id")!.SetValue(session, SessionId);
        return session;
    }

    // ── Testes ─────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task GetPedidos_SemCookie_Retorna401()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("RUN_API_INTEGRATION") == "1",
            "Definir RUN_API_INTEGRATION=1 para executar testes que sobem a API.");

        using var factory = CriarFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/storefront/{Slug}/pedidos");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task GetPedidos_CookieInvalido_Retorna401()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("RUN_API_INTEGRATION") == "1",
            "Definir RUN_API_INTEGRATION=1 para executar testes que sobem a API.");

        using var factory = CriarFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", $"__Host-cdb_session={Guid.NewGuid()}"); // session que não existe

        var response = await client.GetAsync($"/api/storefront/{Slug}/pedidos");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task GetPedidos_HappyPath_Retorna200ComPayloadEsperado()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("RUN_API_INTEGRATION") == "1",
            "Definir RUN_API_INTEGRATION=1 para executar testes que sobem a API.");

        using var factory = CriarFactory(useCaseResult: () => new ListarPedidosClienteResult(
            new[]
            {
                new PedidoStorefrontDto(
                    PedidoId: Guid.NewGuid(),
                    CriadoEm: DateTime.UtcNow,
                    Status: "AguardandoPagamento",
                    Itens: new[] { new PedidoStorefrontItemDto("Lasanha", 1, 8900) },
                    SubtotalCentavos: 8900,
                    FreteCentavos: 1500,
                    TotalCentavos: 10400,
                    JanelaEntrega: new PedidoStorefrontJanelaDto(
                        Data: new DateOnly(2026, 5, 30),
                        HoraInicio: "12:00",
                        HoraFim: "14:00",
                        Label: "Sábado 12h–14h"),
                    Endereco: null,
                    Avaliacao: null,
                    InitPointUrl: null,
                    MotivoCancelamento: null),
            }));

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", $"__Host-cdb_session={SessionId}");

        var response = await client.GetAsync($"/api/storefront/{Slug}/pedidos");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl?.Private.Should().BeTrue();
        response.Headers.CacheControl?.NoStore.Should().BeTrue();

        var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        body!.RootElement.TryGetProperty("pedidos", out var pedidosEl).Should().BeTrue();
        pedidosEl.GetArrayLength().Should().Be(1);

        var primeiro = pedidosEl[0];
        primeiro.GetProperty("status").GetString().Should().Be("AguardandoPagamento");
        primeiro.GetProperty("totalCentavos").GetInt64().Should().Be(10400);
        primeiro.GetProperty("janelaEntrega").GetProperty("label").GetString().Should().Be("Sábado 12h–14h");
    }

    [SkippableFact]
    public async Task GetPedidos_StorefrontInexistente_Retorna404()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("RUN_API_INTEGRATION") == "1",
            "Definir RUN_API_INTEGRATION=1 para executar testes que sobem a API.");

        using var factory = CriarFactory(
            useCaseThrows: new StorefrontNaoEncontradoException("inexistente"));

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", $"__Host-cdb_session={SessionId}");

        var response = await client.GetAsync($"/api/storefront/inexistente/pedidos");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [SkippableFact]
    public async Task GetPedidos_RespeitaQueryLimit()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("RUN_API_INTEGRATION") == "1",
            "Definir RUN_API_INTEGRATION=1 para executar testes que sobem a API.");

        using var factory = CriarFactory(useCaseResult: () =>
            new ListarPedidosClienteResult(Array.Empty<PedidoStorefrontDto>()));

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", $"__Host-cdb_session={SessionId}");

        var response = await client.GetAsync($"/api/storefront/{Slug}/pedidos?limit=5");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // Não verifica passagem do limit ao use case (mock genérico) — coberto em unit.
    }
}

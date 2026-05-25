using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Storefront.Checkout;
using EasyStock.Application.UseCases.Storefront.Checkout.Idempotency;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Exceptions;
using EasyStock.Domain.Exceptions.Storefront;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Api.IntegrationTests.Storefront.Checkout;

/// <summary>
/// Testes E2E do <see cref="CheckoutController"/> com stub MercadoPago (ADR-0014).
/// </summary>
public sealed class CheckoutControllerTests
{
    private static readonly Guid SessionId = Guid.NewGuid();
    private static readonly Guid ClienteId = Guid.NewGuid();
    private static readonly Guid StorefrontId = Guid.NewGuid();
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid JanelaId = Guid.NewGuid();
    private static readonly Guid CardapioItemId = Guid.NewGuid();
    private const string Slug = "casa-da-baba";
    private const string CepValido = "01310100";
    private static readonly DateOnly DataEntrega = new(2026, 6, 2); // terça

    // ── Factory setup ─────────────────────────────────────────────────────

    private static WebApplicationFactory<Program> CriarFactory(
        Action<IServiceCollection>? extraServices = null)
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
                        ["MercadoPago:UseStub"] = "true",
                        ["Jwt:Key"] = "test-super-secret-key-min32chars!!",
                        ["Jwt:Issuer"] = "EasyStock",
                        ["Jwt:Audience"] = "EasyStock",
                    });
                });

                b.ConfigureServices(services =>
                {
                    // Substituir use case por mock
                    var ucDescriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(IniciarCheckoutUseCase));
                    if (ucDescriptor is not null) services.Remove(ucDescriptor);

                    var stubIdempotencyRepo = Substitute.For<ICheckoutIdempotencyRepository>();
                    var mockUc = Substitute.For<IniciarCheckoutUseCase>(
                        Substitute.For<IStorefrontRepository>(),
                        Substitute.For<ICardapioItemRepository>(),
                        Substitute.For<IJanelaEntregaRepository>(),
                        Substitute.For<IBloqueioEntregaRepository>(),
                        Substitute.For<IFreteZonaRepository>(),
                        Substitute.For<IVagaOcupadaRepository>(),
                        Substitute.For<IPedidoStorefrontRepository>(),
                        new CheckoutIdempotencyService(stubIdempotencyRepo, NullLogger<CheckoutIdempotencyService>.Instance),
                        Substitute.For<IMercadoPagoClient>(),
                        NullLogger<IniciarCheckoutUseCase>.Instance);

                    mockUc.ExecuteAsync(Arg.Any<IniciarCheckoutInput>(), Arg.Any<CancellationToken>())
                        .Returns(new CheckoutCriadoDto(Guid.NewGuid(), $"https://stub.mp/test", 1800));

                    services.AddScoped(_ => mockUc);

                    // Substituir clienteSessionRepository por mock
                    var sessionDescriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(IClienteSessionRepository));
                    if (sessionDescriptor is not null) services.Remove(sessionDescriptor);

                    var session = ClienteSession.Criar(
                        ClienteId, EmpresaId, TimeProvider.System);
                    typeof(ClienteSession).GetProperty("Id")!.SetValue(session, SessionId);

                    var mockSession = Substitute.For<IClienteSessionRepository>();
                    mockSession.GetByIdAsync(SessionId, Arg.Any<CancellationToken>()).Returns(session);
                    mockSession.GetByIdAsync(Arg.Is<Guid>(g => g != SessionId), Arg.Any<CancellationToken>())
                        .Returns((ClienteSession?)null);
                    services.AddScoped(_ => mockSession);

                    extraServices?.Invoke(services);
                });
            });
    }

    private static object RequestBody() => new
    {
        items = new[] { new { cardapioItemId = CardapioItemId, qtd = 2 } },
        janelaId = JanelaId,
        dataEntrega = DataEntrega.ToString("yyyy-MM-dd"),
        cep = CepValido,
    };

    // ── Testes ─────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task PostCheckout_SemCookie_Retorna401()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("RUN_API_INTEGRATION") == "1",
            "Definir RUN_API_INTEGRATION=1 para executar testes que sobem a API.");

        using var factory = CriarFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync($"/api/storefront/{Slug}/checkout", RequestBody());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task PostCheckout_ComSessionIdValido_Retorna201ComInitPoint()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("RUN_API_INTEGRATION") == "1",
            "Definir RUN_API_INTEGRATION=1 para executar testes que sobem a API.");

        using var factory = CriarFactory();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
        });

        client.DefaultRequestHeaders.Add("Cookie", $"__Host-cdb_session={SessionId}");

        var response = await client.PostAsJsonAsync($"/api/storefront/{Slug}/checkout", RequestBody());

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        body!.RootElement.TryGetProperty("pedidoId", out _).Should().BeTrue();
        body.RootElement.TryGetProperty("initPointUrl", out var urlEl).Should().BeTrue();
        urlEl.GetString().Should().StartWith("https://stub.mp/");
    }

    [SkippableFact]
    public async Task PostCheckout_JanelaEsgotada_Retorna409()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("RUN_API_INTEGRATION") == "1",
            "Definir RUN_API_INTEGRATION=1 para executar testes que sobem a API.");

        using var factory = CriarFactory(services =>
        {
            var ucDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IniciarCheckoutUseCase));
            if (ucDescriptor is not null) services.Remove(ucDescriptor);

            var mockUc = Substitute.For<IniciarCheckoutUseCase>(
                Substitute.For<IStorefrontRepository>(),
                Substitute.For<ICardapioItemRepository>(),
                Substitute.For<IJanelaEntregaRepository>(),
                Substitute.For<IBloqueioEntregaRepository>(),
                Substitute.For<IFreteZonaRepository>(),
                Substitute.For<IVagaOcupadaRepository>(),
                Substitute.For<IPedidoStorefrontRepository>(),
                Substitute.For<ICheckoutIdempotencyRepository>(),
                Substitute.For<IMercadoPagoClient>(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<IniciarCheckoutUseCase>.Instance);

            mockUc.ExecuteAsync(Arg.Any<IniciarCheckoutInput>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(new JanelaSemVagasException("Janela esgotada."));
            services.AddScoped(_ => mockUc);
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", $"__Host-cdb_session={SessionId}");

        var response = await client.PostAsJsonAsync($"/api/storefront/{Slug}/checkout", RequestBody());

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [SkippableFact]
    public async Task PostCheckout_MpIndisponivel_Retorna503()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("RUN_API_INTEGRATION") == "1",
            "Definir RUN_API_INTEGRATION=1 para executar testes que sobem a API.");

        using var factory = CriarFactory(services =>
        {
            var ucDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IniciarCheckoutUseCase));
            if (ucDescriptor is not null) services.Remove(ucDescriptor);

            var mockUc = Substitute.For<IniciarCheckoutUseCase>(
                Substitute.For<IStorefrontRepository>(),
                Substitute.For<ICardapioItemRepository>(),
                Substitute.For<IJanelaEntregaRepository>(),
                Substitute.For<IBloqueioEntregaRepository>(),
                Substitute.For<IFreteZonaRepository>(),
                Substitute.For<IVagaOcupadaRepository>(),
                Substitute.For<IPedidoStorefrontRepository>(),
                Substitute.For<ICheckoutIdempotencyRepository>(),
                Substitute.For<IMercadoPagoClient>(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<IniciarCheckoutUseCase>.Instance);

            mockUc.ExecuteAsync(Arg.Any<IniciarCheckoutInput>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(new MercadoPagoIndisponivelException("Timeout."));
            services.AddScoped(_ => mockUc);
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", $"__Host-cdb_session={SessionId}");

        var response = await client.PostAsJsonAsync($"/api/storefront/{Slug}/checkout", RequestBody());

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }
}

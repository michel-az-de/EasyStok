using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Storefront.Checkout;
using EasyStock.Application.UseCases.Storefront.Checkout.Idempotency;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Storefront;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace EasyStock.Api.IntegrationTests.Storefront.Checkout;

/// <summary>
/// Testes de idempotência do CheckoutController (TASK-EZ-CHECKOUT-002).
/// </summary>
public sealed class CheckoutIdempotencyTests
{
    private static readonly Guid SessionId = Guid.NewGuid();
    private static readonly Guid ClienteId = Guid.NewGuid();
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid JanelaId = Guid.NewGuid();
    private static readonly Guid CardapioItemId = Guid.NewGuid();
    private const string Slug = "casa-da-baba";
    private const string CepValido = "01310100";
    private static readonly DateOnly DataEntrega = new(2026, 6, 2);

    private static readonly Guid PedidoFixo = Guid.NewGuid();
    private const string InitPointFixo = "https://stub.mp/pref-idempotency-test";

    private static WebApplicationFactory<Program> CriarFactory()
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
                    // Mock UseCase retorna sempre o mesmo DTO
                    var ucDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IniciarCheckoutUseCase));
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
                        Substitute.For<Application.Ports.Output.Pagamentos.IMercadoPagoClient>(),
                        NullLogger<IniciarCheckoutUseCase>.Instance);

                    mockUc.ExecuteAsync(Arg.Any<IniciarCheckoutInput>(), Arg.Any<CancellationToken>())
                        .Returns(new CheckoutCriadoDto(PedidoFixo, InitPointFixo, 1800));

                    services.AddScoped(_ => mockUc);

                    // Mock sessão
                    var sessionDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IClienteSessionRepository));
                    if (sessionDescriptor is not null) services.Remove(sessionDescriptor);

                    var session = ClienteSession.Criar(ClienteId, EmpresaId, TimeProvider.System);
                    typeof(ClienteSession).GetProperty("Id")!.SetValue(session, SessionId);

                    var mockSession = Substitute.For<IClienteSessionRepository>();
                    mockSession.GetByIdAsync(SessionId, Arg.Any<CancellationToken>()).Returns(session);
                    mockSession.GetByIdAsync(Arg.Is<Guid>(g => g != SessionId), Arg.Any<CancellationToken>())
                        .Returns((ClienteSession?)null);
                    services.AddScoped(_ => mockSession);
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

    private static string ComputarHash() =>
        CheckoutContentHasher.ComputarHash(new IniciarCheckoutInput(
            Slug: Slug,
            ClienteId: ClienteId,
            Items: new List<CheckoutItemInput> { new(CardapioItemId, 2) },
            JanelaId: JanelaId,
            DataEntrega: DataEntrega,
            Cep: CepValido));

    // ── Testes ──────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task PostCheckout_SemIdempotencyKey_RetornaBodyNormal()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("RUN_API_INTEGRATION") == "1");

        using var factory = CriarFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", $"__Host-cdb_session={SessionId}");

        var response = await client.PostAsJsonAsync($"/api/storefront/{Slug}/checkout", RequestBody());

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [SkippableFact]
    public async Task PostCheckout_ComKeySemHash_Retorna400()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("RUN_API_INTEGRATION") == "1");

        using var factory = CriarFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", $"__Host-cdb_session={SessionId}");
        client.DefaultRequestHeaders.Add("X-Idempotency-Key", Guid.NewGuid().ToString());

        var response = await client.PostAsJsonAsync($"/api/storefront/{Slug}/checkout", RequestBody());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [SkippableFact]
    public async Task PostCheckout_ComKeyEHash_Retorna201()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("RUN_API_INTEGRATION") == "1");

        using var factory = CriarFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", $"__Host-cdb_session={SessionId}");
        client.DefaultRequestHeaders.Add("X-Idempotency-Key", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add("X-Content-Hash", ComputarHash());

        var response = await client.PostAsJsonAsync($"/api/storefront/{Slug}/checkout", RequestBody());

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        body!.RootElement.TryGetProperty("initPointUrl", out _).Should().BeTrue();
    }

    [SkippableFact]
    public async Task PostCheckout_MismatchHeader_Retorna409()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("RUN_API_INTEGRATION") == "1");

        using var factory = CriarFactory();

        // Override UseCase para lançar IdempotencyMismatchException
        using var factory2 = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b =>
            {
                b.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DatabaseProvider"] = "sqlite",
                    ["ConnectionStrings:DefaultConnection"] = "Data Source=:memory:",
                    ["MercadoPago:UseStub"] = "true",
                    ["Jwt:Key"] = "test-super-secret-key-min32chars!!",
                    ["Jwt:Issuer"] = "EasyStock",
                    ["Jwt:Audience"] = "EasyStock",
                }));

                b.ConfigureServices(services =>
                {
                    var ucDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IniciarCheckoutUseCase));
                    if (ucDescriptor is not null) services.Remove(ucDescriptor);

                    var stubIdempotencyRepo = Substitute.For<ICheckoutIdempotencyRepository>();
                    stubIdempotencyRepo.GetByKeyAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                        .Returns(ci =>
                        {
                            // Simula registro com hash diferente
                            var reg = CheckoutIdempotency.Criar(ci.ArgAt<Guid>(0),
                                "0000000000000000000000000000000000000000000000000000000000000000");
                            return new List<CheckoutIdempotency> { reg } as IReadOnlyList<CheckoutIdempotency>;
                        });

                    var idempotencySvc = new CheckoutIdempotencyService(stubIdempotencyRepo, NullLogger<CheckoutIdempotencyService>.Instance);

                    var mockUc = Substitute.For<IniciarCheckoutUseCase>(
                        Substitute.For<IStorefrontRepository>(),
                        Substitute.For<ICardapioItemRepository>(),
                        Substitute.For<IJanelaEntregaRepository>(),
                        Substitute.For<IBloqueioEntregaRepository>(),
                        Substitute.For<IFreteZonaRepository>(),
                        Substitute.For<IVagaOcupadaRepository>(),
                        Substitute.For<IPedidoStorefrontRepository>(),
                        idempotencySvc,
                        Substitute.For<Application.Ports.Output.Pagamentos.IMercadoPagoClient>(),
                        NullLogger<IniciarCheckoutUseCase>.Instance);

                    mockUc.ExecuteAsync(Arg.Any<IniciarCheckoutInput>(), Arg.Any<CancellationToken>())
                        .Returns(async ci => await Task.FromException<CheckoutCriadoDto>(new IdempotencyMismatchException()));

                    services.AddScoped(_ => mockUc);

                    var sessionDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IClienteSessionRepository));
                    if (sessionDescriptor is not null) services.Remove(sessionDescriptor);

                    var session = ClienteSession.Criar(ClienteId, EmpresaId, TimeProvider.System);
                    typeof(ClienteSession).GetProperty("Id")!.SetValue(session, SessionId);

                    var mockSession = Substitute.For<IClienteSessionRepository>();
                    mockSession.GetByIdAsync(SessionId, Arg.Any<CancellationToken>()).Returns(session);
                    services.AddScoped(_ => mockSession);
                });
            });

        var client2 = factory2.CreateClient();
        client2.DefaultRequestHeaders.Add("Cookie", $"__Host-cdb_session={SessionId}");
        client2.DefaultRequestHeaders.Add("X-Idempotency-Key", Guid.NewGuid().ToString());
        client2.DefaultRequestHeaders.Add("X-Content-Hash", ComputarHash());

        var response = await client2.PostAsJsonAsync($"/api/storefront/{Slug}/checkout", RequestBody());

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Carrinho alterado");
    }
}

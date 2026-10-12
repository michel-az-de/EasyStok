using System.Net;
using EasyStock.Api.Configuration;
using EasyStock.Api.Controllers.Storefront;
using EasyStock.Application.UseCases.Atendimento.ChatSite;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;

namespace EasyStock.Api.IntegrationTests;

public sealed class ComandaRateLimitTests
{
    private static async Task<WebApplication> Host()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddEasyStockRateLimit();
        var app = builder.Build();
        app.Use(async (context, proximo) => { context.Connection.RemoteIpAddress = IPAddress.Loopback; await proximo(); });
        app.UseRouting();
        app.UseRateLimiter();
        foreach (var (acao, caminho) in new[] { (nameof(ComandaClienteController.Obter), "comanda"),
            (nameof(ComandaClienteController.Cardapio), "cardapio"), (nameof(ComandaClienteController.Janelas), "janelas") })
        {
            var policy = typeof(ComandaClienteController).GetMethod(acao)!.GetCustomAttributes(typeof(EnableRateLimitingAttribute), true)
                .Cast<EnableRateLimitingAttribute>().Single().PolicyName!;
            app.MapGet($"/api/storefront/cardapio-conversa/{{token}}/{caminho}", () => "ok").RequireRateLimiting(policy);
        }
        app.MapGet("/cadastro", () => "ok").RequireRateLimiting("disponibilidade");
        await app.StartAsync();
        return app;
    }

    [Fact]
    public async Task Leitura_30ConsultasDaMesmaComandaNaoBloqueiamOutrosClientesNoMesmoIp()
    {
        await using var app = await Host();
        using var client = app.GetTestClient();
        var tokens = Enumerable.Range(0, 4).Select(_ => AcessoChatSite.NovoToken()).ToArray();
        for (var i = 0; i < 30; i++)
            (await client.GetAsync($"/api/storefront/cardapio-conversa/{tokens[0]}/{new[] { "comanda", "cardapio", "janelas" }[i % 3]}"))
                .StatusCode.Should().Be(HttpStatusCode.OK);
        using var limitado = await client.GetAsync($"/api/storefront/cardapio-conversa/{tokens[0]}/comanda");
        limitado.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        limitado.Headers.Should().ContainKey("Retry-After");
        foreach (var token in tokens.Skip(1))
            (await client.GetAsync($"/api/storefront/cardapio-conversa/{token}/comanda")).StatusCode.Should().Be(HttpStatusCode.OK,
                "cada cliente conserva a própria quota no mesmo Wi-Fi");
    }

    [Fact]
    public async Task TokensMalformadosCompartilhamFallbackPorIp()
    {
        await using var app = await Host();
        using var client = app.GetTestClient();
        for (var i = 0; i < 30; i++)
            (await client.GetAsync($"/api/storefront/cardapio-conversa/invalido-{i}/comanda")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/api/storefront/cardapio-conversa/outro-invalido/comanda")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await client.GetAsync($"/api/storefront/cardapio-conversa/{AcessoChatSite.NovoToken()}/comanda")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DisponibilidadeDoCadastroContinuaLimitadaPorIp()
    {
        await using var app = await Host();
        using var client = app.GetTestClient();
        for (var i = 0; i < 30; i++) (await client.GetAsync("/cadastro")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/cadastro")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }
}

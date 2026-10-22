using System.Net;
using EasyStock.Api.Configuration;
using EasyStock.Application.UseCases.Atendimento.ChatSite;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;

namespace EasyStock.Api.IntegrationTests;

/// <summary>
/// #1622-1, #1643-2, #1725-1 e #1725-2: as rotas públicas do chat do site e da comanda limitam por sessão ou token para
/// um visitante não gastar a cota do outro atrás do mesmo NAT. Só que token inventado a cada requisição abria partição
/// nova. O teto por IP vem encadeado: inventar token não passa dele, e outro IP segue livre.
/// </summary>
public sealed class LimitePorIpPublicoTests
{
    private const string CabecalhoIp = "X-Teste-Ip";

    private static async Task<WebApplication> Host()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddEasyStockRateLimit();
        var app = builder.Build();
        app.Use(async (context, proximo) =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(context.Request.Headers[CabecalhoIp].FirstOrDefault() ?? "10.0.0.1");
            await proximo();
        });
        app.UseRouting();
        app.UseRateLimiter();
        app.MapPost("/api/public/chat/{slug}/mensagens", () => "ok").RequireRateLimiting(ChatSiteRateLimit.Mensagem);
        app.MapGet("/api/public/chat/{slug}/stream", async (HttpContext http) =>
        {
            await http.Response.WriteAsync("aberto");
            await http.Response.Body.FlushAsync();
            try { await Task.Delay(Timeout.Infinite, http.RequestAborted); } catch (OperationCanceledException) { }
        }).RequireRateLimiting(ChatSiteRateLimit.Stream);
        app.MapPost("/api/storefront/cardapio-conversa/{token}/confirmar", () => "ok").RequireRateLimiting("comanda-envio");
        await app.StartAsync();
        return app;
    }

    private static HttpRequestMessage Pedido(HttpMethod metodo, string url, string ip, string? token = null)
    {
        var pedido = new HttpRequestMessage(metodo, url);
        pedido.Headers.Add(CabecalhoIp, ip);
        if (token is not null) pedido.Headers.Add(ChatSiteRateLimit.HeaderToken, token);
        return pedido;
    }

    [Fact]
    public async Task ChatDoSite_TokenInventadoACadaMensagemBateNoTetoPorIp()
    {
        await using var app = await Host();
        using var client = app.GetTestClient();
        var respostas = new List<HttpStatusCode>();
        for (var i = 0; i <= ChatSiteRateLimit.MensagensPorIpPorMinuto; i++)
        {
            using var resposta = await client.SendAsync(Pedido(HttpMethod.Post, "/api/public/chat/casa/mensagens", "10.0.0.1",
                AcessoChatSite.NovoToken()));
            respostas.Add(resposta.StatusCode);
        }

        respostas.Take(ChatSiteRateLimit.MensagensPorIpPorMinuto).Should().OnlyContain(s => s == HttpStatusCode.OK);
        respostas[^1].Should().Be(HttpStatusCode.TooManyRequests, "inventar token não abre cota nova no mesmo IP");
        using var outroIp = await client.SendAsync(Pedido(HttpMethod.Post, "/api/public/chat/casa/mensagens", "10.0.0.2",
            AcessoChatSite.NovoToken()));
        outroIp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Comanda_TokenInventadoACadaEnvioBateNoTetoPorIp()
    {
        await using var app = await Host();
        using var client = app.GetTestClient();
        for (var i = 0; i < ChatSiteRateLimit.ComandaEnviosPorIpPorMinuto; i++)
        {
            using var ok = await client.SendAsync(Pedido(HttpMethod.Post,
                $"/api/storefront/cardapio-conversa/{AcessoChatSite.NovoToken()}/confirmar", "10.0.0.3"));
            ok.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using var limitado = await client.SendAsync(Pedido(HttpMethod.Post,
            $"/api/storefront/cardapio-conversa/{AcessoChatSite.NovoToken()}/confirmar", "10.0.0.3"));
        limitado.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        limitado.Headers.Should().ContainKey("Retry-After");
    }

    [Fact]
    public async Task ChatDoSite_StreamsAbertosPorIpTemTeto()
    {
        await using var app = await Host();
        using var client = app.GetTestClient();
        var abertos = new List<HttpResponseMessage>();
        try
        {
            for (var i = 0; i < ChatSiteRateLimit.StreamsPorIp; i++)
            {
                var resposta = await client.SendAsync(Pedido(HttpMethod.Get, "/api/public/chat/casa/stream", "10.0.0.4",
                    AcessoChatSite.NovoToken()), HttpCompletionOption.ResponseHeadersRead);
                abertos.Add(resposta);
                resposta.StatusCode.Should().Be(HttpStatusCode.OK);
            }

            using var limitado = await client.SendAsync(Pedido(HttpMethod.Get, "/api/public/chat/casa/stream", "10.0.0.4",
                AcessoChatSite.NovoToken()), HttpCompletionOption.ResponseHeadersRead);
            limitado.StatusCode.Should().Be(HttpStatusCode.TooManyRequests, "cada stream consulta o banco a cada segundo");
        }
        finally
        {
            foreach (var resposta in abertos) resposta.Dispose();
        }
    }
}

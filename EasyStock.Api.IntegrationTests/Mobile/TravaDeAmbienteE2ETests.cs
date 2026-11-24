using System.Net;
using System.Net.Http.Json;
using EasyStock.Api.Middleware;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace EasyStock.Api.IntegrationTests.Mobile;

/// <summary>
/// Issue #3251 (P2 da #3114) na API de verdade: com a trava ligada, como na produção, o cliente que se
/// declara homologação é recusado antes de autenticar, parear e sincronizar. Sem o cabeçalho nada muda.
/// </summary>
[Collection("MobileE2E")]
public class TravaDeAmbienteE2ETests(MobileE2EFixture fixture)
{
    [SkippableFact]
    public async Task Producao_recusa_a_homologacao_antes_de_autenticar_parear_e_sincronizar()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (empresa, loja) = await fixture.SeedEmpresaELojaAsync();
        var aparelho = await fixture.SeedMobileDeviceAsync(empresa, loja);
        var pull = $"/api/mobile/sync/pull?since=0&deviceId={aparelho.DeviceId}";
        // Por cima do appsettings.Development.json, como a produção, que não tem a chave.
        await using var producao = fixture.Factory!.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, cfg) =>
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [TravaDeAmbienteMiddleware.ChaveAceitaHomologacao] = "false",
            })));

        using var oficial = producao.CreateClient();
        oficial.DefaultRequestHeaders.Add("X-Mobile-Api-Key", aparelho.ApiKey);
        (await oficial.GetAsync(pull)).StatusCode.Should().Be(HttpStatusCode.OK, "sem o cabeçalho nada muda");

        using var homologacao = producao.CreateClient();
        homologacao.DefaultRequestHeaders.Add("X-Mobile-Api-Key", aparelho.ApiKey);
        homologacao.DefaultRequestHeaders.Add(TravaDeAmbienteMiddleware.Cabecalho, "homologacao");
        await DeveRecusar(await homologacao.GetAsync(pull), "credencial válida não libera o sync");

        using var anonimo = producao.CreateClient();
        (await anonimo.GetAsync(pull)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        anonimo.DefaultRequestHeaders.Add(TravaDeAmbienteMiddleware.Cabecalho, "homologacao");
        await DeveRecusar(await anonimo.GetAsync(pull), "403 e não 401: a recusa vem antes da autenticação");
        await DeveRecusar(await anonimo.PostAsJsonAsync("/api/mobile/devices/pair", new
        {
            pairingCode = "000000", deviceId = "apk-" + Guid.NewGuid().ToString("N"),
        }), "a recusa vem antes do pareamento");

        // CORS real da API, com a origem do WebView do APK (https://localhost).
        using var webView = producao.CreateClient();
        using var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/mobile/devices/pair");
        preflight.Headers.Add("Origin", "https://localhost");
        preflight.Headers.Add("Access-Control-Request-Method", "POST");
        preflight.Headers.Add("Access-Control-Request-Headers", "x-cdb-ambiente,content-type");
        var liberado = await webView.SendAsync(preflight);
        liberado.StatusCode.Should().Be(HttpStatusCode.NoContent);
        string.Join(",", liberado.Headers.GetValues("Access-Control-Allow-Headers")).Should().ContainEquivalentOf("x-cdb-ambiente");

        webView.DefaultRequestHeaders.Add("Origin", "https://localhost");
        webView.DefaultRequestHeaders.Add(TravaDeAmbienteMiddleware.Cabecalho, "homologacao");
        var recusa = await webView.PostAsJsonAsync("/api/mobile/devices/pair", new { pairingCode = "000000", deviceId = "apk-x" });
        await DeveRecusar(recusa, "o WebView recebe a recusa");
        recusa.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle()
            .Which.Should().Be("https://localhost", "sem a origem o WebView esconde o motivo da recusa");
    }

    [SkippableFact]
    public async Task Desenvolvimento_aceita_a_homologacao()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (empresa, loja) = await fixture.SeedEmpresaELojaAsync();
        var aparelho = await fixture.SeedMobileDeviceAsync(empresa, loja);
        using var cliente = fixture.CreateMobileClient(aparelho.ApiKey);
        cliente.DefaultRequestHeaders.Add(TravaDeAmbienteMiddleware.Cabecalho, "homologacao");

        (await cliente.GetAsync($"/api/mobile/sync/pull?since=0&deviceId={aparelho.DeviceId}"))
            .StatusCode.Should().Be(HttpStatusCode.OK, "o laboratório roda em Development com a chave");
    }

    private static async Task DeveRecusar(HttpResponseMessage resposta, string motivo)
    {
        resposta.StatusCode.Should().Be(HttpStatusCode.Forbidden, motivo);
        (await resposta.Content.ReadAsStringAsync()).Should().Contain("AMBIENTE_RECUSADO", motivo);
    }
}

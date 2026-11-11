using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Api.IntegrationTests.Mobile;

/// <summary>
/// #2209 (F4 da impressão v2): o aparelho lê a regra de impressão da loja em que está pareado, por
/// <c>GET /api/mobile/impressao</c> com <c>X-Mobile-Api-Key</c>. A loja vem sempre do pareamento: o aparelho não
/// escolhe loja, e o de outra loja ou de outra empresa só vê a regra da sua.
/// </summary>
[Collection("MobileE2E")]
public class MobileImpressaoE2ETests(MobileE2EFixture fixture)
{
    private const string Rota = "/api/mobile/impressao";

    private const string RegraA1 =
        """{"v":1,"documentos":{"etiqueta-producao":{"papel":"etiqueta-52","template":null,"orientacao":"vertical","codigos":["qr","barras"],"copias":2}}}""";
    private const string RegraA2 =
        """{"v":1,"documentos":{"etiqueta-producao":{"papel":"etiqueta-80","template":null,"orientacao":null,"codigos":["qr"],"copias":1}}}""";
    private const string RegraB =
        """{"v":1,"documentos":{"pedido":{"papel":"cupom-58","template":null,"orientacao":null,"codigos":null,"copias":1}}}""";

    [SkippableFact]
    public async Task SemChave_401()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        (await fixture.CreateClient().GetAsync(Rota)).StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "sem a chave não há aparelho, e sem aparelho não há loja: nunca a regra de alguém");
    }

    [SkippableFact]
    public async Task ChaveInvalidaOuRevogada_401()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (empresaId, lojaId) = await fixture.SeedEmpresaELojaAsync();
        await GravarRegraAsync(lojaId, RegraA1, versao: 1);
        var revogado = await fixture.SeedMobileDeviceAsync(empresaId, lojaId, revoked: true);

        (await fixture.CreateMobileClient("tk-" + Guid.NewGuid().ToString("N")).GetAsync(Rota))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await fixture.CreateMobileClient(revogado.ApiKey).GetAsync(Rota))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task LojaNuncaConfigurada_DevolveVersaoZeroERegraNula()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (empresaId, lojaId) = await fixture.SeedEmpresaELojaAsync();
        var aparelho = await fixture.SeedMobileDeviceAsync(empresaId, lojaId);

        var resp = await fixture.CreateMobileClient(aparelho.ApiKey).GetAsync(Rota);

        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
        resp.Headers.ETag!.Tag.Should().Be("\"0\"");
        var data = await DataAsync(resp);
        data.GetProperty("lojaId").GetGuid().Should().Be(lojaId);
        data.GetProperty("versao").GetInt32().Should().Be(0);
        data.GetProperty("impressao").ValueKind.Should().Be(JsonValueKind.Null, "nunca configurada: o aparelho usa o padrão dele");
        data.GetProperty("alteradoEm").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [SkippableFact]
    public async Task RegraGravada_DevolveARegraDaLojaDoPareamento_ComVersaoEETag()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (empresaId, lojaId) = await fixture.SeedEmpresaELojaAsync();
        await GravarRegraAsync(lojaId, RegraA1, versao: 3);
        var aparelho = await fixture.SeedMobileDeviceAsync(empresaId, lojaId);

        var resp = await fixture.CreateMobileClient(aparelho.ApiKey).GetAsync(Rota);

        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
        resp.Headers.ETag!.Tag.Should().Be("\"3\"");
        var data = await DataAsync(resp);
        data.GetProperty("lojaId").GetGuid().Should().Be(lojaId);
        data.GetProperty("versao").GetInt32().Should().Be(3);
        MesmaRegra(data, RegraA1).Should().BeTrue();
        data.GetProperty("alteradoEm").ValueKind.Should().Be(JsonValueKind.String);
    }

    [SkippableFact]
    public async Task Isolamento_AparelhoDeOutraLojaOuEmpresa_SoVeARegraDaSuaLoja_MesmoPedindoOutra()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (empresaA, lojaA1) = await fixture.SeedEmpresaELojaAsync();
        var lojaA2 = await NovaLojaAsync(empresaA);
        var (empresaB, lojaB) = await fixture.SeedEmpresaELojaAsync();
        await GravarRegraAsync(lojaA1, RegraA1, versao: 4);
        await GravarRegraAsync(lojaA2, RegraA2, versao: 1);
        await GravarRegraAsync(lojaB, RegraB, versao: 2);

        var naA2 = await fixture.SeedMobileDeviceAsync(empresaA, lojaA2);
        var naB = await fixture.SeedMobileDeviceAsync(empresaB, lojaB);

        // A chave de outro aparelho pedindo a loja A1 pela query: a loja continua sendo a do pareamento.
        foreach (var (cred, lojaEsperada, regraEsperada) in new[] { (naA2, lojaA2, RegraA2), (naB, lojaB, RegraB) })
        {
            var http = fixture.CreateMobileClient(cred.ApiKey);
            http.DefaultRequestHeaders.Add("X-Loja-Id", lojaA1.ToString());
            var resp = await http.GetAsync($"{Rota}?lojaId={lojaA1}&empresaId={empresaA}");

            resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
            var data = await DataAsync(resp);
            data.GetProperty("lojaId").GetGuid().Should().Be(lojaEsperada, "a loja é a do pareamento, nunca a pedida");
            MesmaRegra(data, regraEsperada).Should().BeTrue();
            MesmaRegra(data, RegraA1).Should().BeFalse("a regra da A1 nunca sai para outro aparelho");
        }
    }

    [SkippableFact]
    public async Task AparelhoTrocaDeLoja_PassaAVerARegraDaLojaNova()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (empresaId, lojaA1) = await fixture.SeedEmpresaELojaAsync();
        var lojaA2 = await NovaLojaAsync(empresaId);
        await GravarRegraAsync(lojaA1, RegraA1, versao: 1);
        await GravarRegraAsync(lojaA2, RegraA2, versao: 1);
        var cred = await fixture.SeedMobileDeviceAsync(empresaId, lojaA1);

        await TrocarLojaNoBancoAsync(cred.DeviceId, lojaA2);
        var data = await DataAsync(await fixture.CreateMobileClient(cred.ApiKey).GetAsync(Rota));

        data.GetProperty("lojaId").GetGuid().Should().Be(lojaA2);
        MesmaRegra(data, RegraA2).Should().BeTrue();
    }

    // ---------- apoio ----------

    private static async Task<JsonElement> DataAsync(HttpResponseMessage resp)
    {
        var corpo = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return corpo.GetProperty("data");
    }

    private static bool MesmaRegra(JsonElement data, string regra) =>
        JsonNode.DeepEquals(JsonNode.Parse(data.GetProperty("impressao").GetRawText()), JsonNode.Parse(regra));

    private async Task GravarRegraAsync(Guid lojaId, string regra, int versao)
    {
        using var scope = fixture.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        var config = ConfiguracaoLoja.CriarPadrao(lojaId);
        config.ImpressaoJson = regra;
        config.ImpressaoVersao = versao;
        config.ImpressaoAlteradaEm = DateTime.UtcNow;
        config.CriadoEm = DateTime.UtcNow;
        config.AlteradoEm = DateTime.UtcNow;
        db.ConfiguracoesLoja.Add(config);
        await db.SaveChangesAsync();
    }

    private async Task<Guid> NovaLojaAsync(Guid empresaId)
    {
        using var scope = fixture.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        var loja = new Loja
        {
            Id = Guid.NewGuid(), EmpresaId = empresaId, Nome = "Loja 2", Ativa = true,
            CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow
        };
        db.Lojas.Add(loja);
        await db.SaveChangesAsync();
        return loja.Id;
    }

    private async Task TrocarLojaNoBancoAsync(string deviceId, Guid lojaId)
    {
        using var scope = fixture.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        var device = db.Set<EasyStock.Domain.Entities.Mobile.MobileDevice>().IgnoreQueryFilters().Single(d => d.Id == deviceId);
        device.LojaId = lojaId;
        await db.SaveChangesAsync();
    }
}

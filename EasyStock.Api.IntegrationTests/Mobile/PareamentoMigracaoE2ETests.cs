using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace EasyStock.Api.IntegrationTests.Mobile;

[Collection("MobileE2E")]
public class PareamentoMigracaoE2ETests(MobileE2EFixture fixture)
{
    [SkippableFact]
    public async Task Pareamento_CodigoDoGerenteHabilitaPrimeiraCarga_EConsomeCodigo()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (empresaId, lojaId) = await fixture.SeedEmpresaELojaAsync("Operacao do piloto");
        using var gerente = await GerenteAsync(empresaId);
        var codigo = await CodigoAsync(gerente, empresaId, lojaId);
        var aparelhoId = "apk-" + Guid.NewGuid().ToString("N");
        using var anonimo = fixture.CreateClient();
        var resposta = await anonimo.PostAsJsonAsync("/api/mobile/devices/pair", new
        {
            pairingCode = codigo, deviceId = aparelhoId, label = "APK de teste"
        });
        resposta.StatusCode.Should().Be(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync());
        var corpo = await resposta.Content.ReadFromJsonAsync<JsonElement>();
        corpo.GetProperty("empresaId").GetGuid().Should().Be(empresaId);
        corpo.GetProperty("lojaId").GetGuid().Should().Be(lojaId);
        corpo.GetProperty("deviceId").GetString().Should().Be(aparelhoId);
        corpo.GetProperty("empresaNome").GetString().Should().Be("Operacao do piloto");
        using var pareado = fixture.CreateMobileClient(corpo.GetProperty("apiKey").GetString()!);
        (await pareado.GetAsync($"/api/mobile/sync/pull?since=0&deviceId={aparelhoId}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await anonimo.GetAsync($"/api/mobile/sync/pull?since=0&deviceId={aparelhoId}"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonimo.PostAsJsonAsync("/api/mobile/devices/pair", new
        {
            pairingCode = codigo, deviceId = "outro-" + Guid.NewGuid().ToString("N")
        })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task Pareamento_GerenteNaoEmiteCodigoParaOutraEmpresaOuLoja()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (empresa, loja) = await fixture.SeedEmpresaELojaAsync();
        var (outraEmpresa, outraLoja) = await fixture.SeedEmpresaELojaAsync();
        using var gerente = await GerenteAsync(empresa);
        (await gerente.PostAsJsonAsync("/api/mobile/devices/pair-codes", new
        {
            empresaId = outraEmpresa, lojaId = outraLoja
        })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await gerente.PostAsJsonAsync("/api/mobile/devices/pair-codes", new
        {
            empresaId = empresa, lojaId = outraLoja
        })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodigoAsync(gerente, empresa, loja)).Should().HaveLength(6);
    }

    [SkippableFact]
    public async Task Repareamento_TrocaChaveSemLiberarAChaveAntiga()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (empresa, loja) = await fixture.SeedEmpresaELojaAsync();
        var anterior = await fixture.SeedMobileDeviceAsync(empresa, loja);
        using var gerente = await GerenteAsync(empresa);
        var codigo = await CodigoAsync(gerente, empresa, loja);
        using var anonimo = fixture.CreateClient();
        var resposta = await anonimo.PostAsJsonAsync("/api/mobile/devices/pair", new
        {
            pairingCode = codigo, deviceId = anterior.DeviceId, label = "Aparelho reconectado"
        });
        resposta.StatusCode.Should().Be(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync());
        var corpo = await resposta.Content.ReadFromJsonAsync<JsonElement>();
        var novaChave = corpo.GetProperty("apiKey").GetString()!;
        novaChave.Should().NotBe(anterior.ApiKey);
        using var antigo = fixture.CreateMobileClient(anterior.ApiKey);
        using var novo = fixture.CreateMobileClient(novaChave);
        var rota = $"/api/mobile/sync/pull?since=0&deviceId={anterior.DeviceId}";
        (await antigo.GetAsync(rota)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await novo.GetAsync(rota)).StatusCode.Should().Be(HttpStatusCode.OK);
        using var scope = fixture.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        (await db.Set<MobileDevice>().CountAsync(d => d.Id == anterior.DeviceId)).Should().Be(1);
    }

    private static async Task<string> CodigoAsync(HttpClient gerente, Guid empresa, Guid loja)
    {
        var resposta = await gerente.PostAsJsonAsync("/api/mobile/devices/pair-codes", new
        {
            empresaId = empresa, lojaId = loja, label = "Piloto de teste"
        });
        resposta.StatusCode.Should().Be(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync());
        return (await resposta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("pairingCode").GetString()!;
    }

    private async Task<HttpClient> GerenteAsync(Guid empresa)
    {
        Guid usuarioId;
        using (var scope = fixture.Factory!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            using var bypass = db.UseRowLevelSecurityBypass();
            var usuario = Usuario.Criar("Gerente do teste", $"piloto-{Guid.NewGuid():N}@example.test", "sem-login-por-senha");
            db.Usuarios.Add(usuario);
            await db.SaveChangesAsync();
            usuarioId = usuario.Id;
        }
        var claims = new[]
        {
            new Claim("sub", usuarioId.ToString()), new Claim("nivel", "Admin"),
            new Claim("empresaId", empresa.ToString()),
            new Claim("iat", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };
        var chave = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("EasyStock-Test-SuperSecretKey-Min32Chars!!"));
        var jwt = new JwtSecurityToken("EasyStock", "EasyStock", claims, expires: DateTime.UtcNow.AddMinutes(20),
            signingCredentials: new SigningCredentials(chave, SecurityAlgorithms.HmacSha256));
        var cliente = fixture.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(jwt));
        return cliente;
    }
}

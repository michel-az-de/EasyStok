using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using DotNet.Testcontainers.Builders;
using EasyStock.Application.Ports.Output.Integration.Conexao;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;

namespace EasyStock.Api.IntegrationTests.Integracoes;

/// <summary>
/// F16 (#1246) ponta a ponta: API real (WebApplicationFactory) + Postgres real (Testcontainers).
/// O PUT grava a chave cifrada (o texto não está em <c>payload_cifrado</c>), o GET nunca devolve o
/// segredo, sem KEK o PUT responde 503 e o Testar tem teto de 6 por minuto. Os testadores são
/// trocados por um falso: nenhuma chamada sai para provedor. A KEK é gerada no teste.
/// </summary>
public sealed class IntegracoesE2ETests : IAsyncLifetime
{
    private PostgreSqlContainer? _pg;
    private bool _isAvailable;

    private const string JwtIssuer = "EasyStock";
    private const string JwtAudience = "EasyStock";
    private const string JwtSecret = "EasyStock-Test-SuperSecretKey-Min32Chars!!";
    private const string Segredo = "APP_USR-segredo-e2e-4321";

    public async Task InitializeAsync()
    {
        try
        {
            _pg = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("easystock_integracoes_e2e")
                .WithUsername("postgres")
                .WithPassword("e2e-integracoes-senha")
                .Build();
            await _pg.StartAsync();
            _isAvailable = true;
        }
        catch (DockerUnavailableException)
        {
            _isAvailable = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (_pg is not null) await _pg.DisposeAsync();
    }

    private WebApplicationFactory<Program> CriarFactory(bool comKek)
    {
        var config = new Dictionary<string, string?>
        {
            ["Database:Provider"] = "PostgreSql",
            ["ConnectionStrings:DefaultConnection"] = _pg!.GetConnectionString(),
            ["ConnectionStrings:Redis"] = "localhost:6379",
            ["Jwt:Issuer"] = JwtIssuer,
            ["Jwt:Audience"] = JwtAudience,
            ["Jwt:SecretKey"] = JwtSecret,
            ["Jwt:ExpirationMinutes"] = "60",
            ["Anthropic:Enabled"] = "false",
            ["FileStorage:Provider"] = "Local",
            ["MercadoPago:UseStub"] = "true",
            ["BackgroundJobs:EnableVigiaIntegracoes"] = "false",
        };
        if (comKek)
        {
            config["Crypto:CurrentKekId"] = "kek-e2e";
            config["Crypto:Keks:kek-e2e"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        }

        // ConfigureAppConfiguration chega tarde para o Program.cs ler a connection string (mesmo
        // contorno do SeedFlowIntegrationTests).
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", _pg.GetConnectionString());
        Environment.SetEnvironmentVariable("Database__Provider", "PostgreSql");

        return new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(config));
            b.ConfigureTestServices(s =>
            {
                s.RemoveAll<ITestadorIntegracao>();
                s.AddScoped<ITestadorIntegracao, TestadorFalso>();
            });
        });
    }

    private static string GerarJwt(Guid empresaId)
    {
        var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret)), SecurityAlgorithms.HmacSha256);
        var claims = new List<Claim>
        {
            new("sub", Guid.NewGuid().ToString()),
            new("nivel", "Admin"),
            new("empresaId", empresaId.ToString()),
        };
        var token = new JwtSecurityToken(JwtIssuer, JwtAudience, claims, expires: DateTime.UtcNow.AddMinutes(30), signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static async Task<HttpClient> ClienteDaEmpresaAsync(WebApplicationFactory<Program> factory, Guid empresaId)
    {
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            using var _ = db.UseRowLevelSecurityBypass();
            db.Empresas.Add(new Empresa { Id = empresaId, Nome = "E2E Integrações", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GerarJwt(empresaId));
        return http;
    }

    [SkippableFact]
    public async Task SalvarGravaCifradoEGetNuncaDevolveOSegredo()
    {
        Skip.IfNot(_isAvailable, "Docker indisponível");
        await using var factory = CriarFactory(comKek: true);
        var empresaId = Guid.NewGuid();
        var http = await ClienteDaEmpresaAsync(factory, empresaId);

        var put = await http.PutAsJsonAsync("/api/integracoes/mercadopago", new { campos = new { accessToken = Segredo } });
        var corpoPut = await put.Content.ReadAsStringAsync();
        put.StatusCode.Should().Be(HttpStatusCode.OK, corpoPut);
        corpoPut.Should().NotContain(Segredo).And.Contain("\"mascara\":\"4321\"");

        var get = await http.GetStringAsync("/api/integracoes");
        get.Should().NotContain(Segredo).And.Contain("\"origem\":\"loja\"");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        using var _ = db.UseRowLevelSecurityBypass();
        var bruto = await db.Database
            .SqlQuery<byte[]>($"SELECT payload_cifrado AS \"Value\" FROM credencial_integracao WHERE empresa_id = {empresaId}")
            .SingleAsync();
        bruto.AsSpan().IndexOf(Encoding.UTF8.GetBytes(Segredo)).Should().Be(-1, "payload_cifrado não carrega o texto");
    }

    [SkippableFact]
    public async Task SemKekOPutResponde503ComOMotivo()
    {
        Skip.IfNot(_isAvailable, "Docker indisponível");
        await using var factory = CriarFactory(comKek: false);
        var http = await ClienteDaEmpresaAsync(factory, Guid.NewGuid());

        var put = await http.PutAsJsonAsync("/api/integracoes/mercadopago", new { campos = new { accessToken = Segredo } });

        put.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await put.Content.ReadAsStringAsync()).Should().Contain("EZ_CRYPTO_KEK").And.NotContain(Segredo);
    }

    [SkippableFact]
    public async Task TestarTemTetoDe6PorMinutoPorLojaEProvider()
    {
        Skip.IfNot(_isAvailable, "Docker indisponível");
        await using var factory = CriarFactory(comKek: true);
        var http = await ClienteDaEmpresaAsync(factory, Guid.NewGuid());
        (await http.PutAsJsonAsync("/api/integracoes/lalamove",
            new { campos = new { apiKey = "pk_test_e2e_chave", apiSecret = "sk_test_e2e" }, ambiente = "sandbox" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var status = new List<HttpStatusCode>();
        for (var i = 0; i < 7; i++)
            status.Add((await http.PostAsync("/api/integracoes/lalamove/testar", null)).StatusCode);

        status.Take(6).Should().AllBeEquivalentTo(HttpStatusCode.OK);
        status[6].Should().Be(HttpStatusCode.TooManyRequests);
        var get = await http.GetStringAsync("/api/integracoes");
        get.Should().Contain("\"ultimoTesteOk\":true");
    }

    private sealed class TestadorFalso : ITestadorIntegracao
    {
        public string Provider => "lalamove";

        public Task<ResultadoTesteIntegracao> TestarAsync(ChaveParaTeste chave, CancellationToken ct = default) =>
            Task.FromResult(ResultadoTesteIntegracao.Passou("Chave conferida (falso)."));
    }
}

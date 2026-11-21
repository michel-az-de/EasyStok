using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace EasyStock.Api.IntegrationTests.Atendimento;

/// <summary>
/// #2598: a empresa sem loja online abre Cardápio e Produção no console. As leituras respondem 200 com o vazio de
/// sempre e <c>meta.lojaDisponivel = false</c>, como a agenda de entregas; a escrita segue 404, agora com o código
/// estável <c>VITRINE_DESLIGADA</c> no lugar da mensagem que o console casava por regex.
/// </summary>
public sealed class SemVitrineIntegrationTests : IAsyncLifetime
{
    private BancoDeTeste? _pg;
    private bool _isAvailable;
    private static readonly Guid UsuarioId = Guid.NewGuid();

    private const string JwtIssuer = "EasyStock";
    private const string JwtAudience = "EasyStock";
    private const string JwtSecret = "EasyStock-Test-SuperSecretKey-Min32Chars!!";

    public async Task InitializeAsync()
    {
        try
        {
            _pg = new BancoDeTeste();
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

    [SkippableTheory]
    [InlineData("/api/atendimento/comanda/cardapio", "itens")]
    [InlineData("/api/atendimento/comanda/cardapio/gestao", null)]
    [InlineData("/api/atendimento/comanda/cardapio/fora", null)]
    [InlineData("/api/atendimento/comanda/cardapio/secoes", null)]
    [InlineData("/api/atendimento/comanda/cardapio/secoes/migrar-categorias/previa", "categorias")]
    [InlineData("/api/atendimento/producao/receitas", null)]
    [InlineData("/api/atendimento/producao/estoque-do-dia", "pratos")]
    [InlineData("/api/atendimento/producao/sugestao", "pratos")]
    public async Task Leitura_SemVitrine_Responde200VazioComLojaIndisponivel(string rota, string? lista)
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();
        using var client = await ClienteDaEmpresaSemVitrineAsync(factory);

        using var resposta = await client.GetAsync(rota);

        var corpo = await resposta.Content.ReadAsStringAsync();
        resposta.StatusCode.Should().Be(HttpStatusCode.OK, corpo);
        using var json = JsonDocument.Parse(corpo);
        json.RootElement.GetProperty("meta").GetProperty("lojaDisponivel").GetBoolean().Should().BeFalse();
        var data = json.RootElement.GetProperty("data");
        var vazio = lista is null ? data : data.GetProperty(lista);
        vazio.ValueKind.Should().Be(JsonValueKind.Array, corpo);
        vazio.GetArrayLength().Should().Be(0);
    }

    [SkippableFact]
    public async Task Escrita_SemVitrine_Responde404ComCodigoEstavel()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();
        using var client = await ClienteDaEmpresaSemVitrineAsync(factory);

        using var resposta = await client.PostAsync($"/api/atendimento/producao/receitas/pratos/{Guid.NewGuid()}/ligar", null);

        var corpo = await resposta.Content.ReadAsStringAsync();
        resposta.StatusCode.Should().Be(HttpStatusCode.NotFound, corpo);
        using var json = JsonDocument.Parse(corpo);
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("VITRINE_DESLIGADA");
    }

    private static async Task<HttpClient> ClienteDaEmpresaSemVitrineAsync(WebApplicationFactory<Program> factory)
    {
        var empresaId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            using var _ = db.UseRowLevelSecurityBypass();
            if (!db.Usuarios.Any(u => u.Id == UsuarioId))
            {
                var usuario = Usuario.Criar("Sem Vitrine", "sem-vitrine@example.test", "sem-login-por-senha");
                usuario.Id = UsuarioId;
                db.Usuarios.Add(usuario);
            }
            db.Empresas.Add(new Empresa { Id = empresaId, Nome = "Sem Vitrine", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GerarJwt(empresaId));
        return client;
    }

    private WebApplicationFactory<Program> CriarFactory()
    {
        if (_pg is null) throw new InvalidOperationException("Conteiner PostgreSQL nao disponivel.");
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b =>
            {
                b.UseEnvironment("Development");
                b.UseSetting("ConnectionStrings:DefaultConnection", _pg.GetConnectionString());
                b.UseSetting("Database:Provider", "PostgreSql");
                b.UseSetting("Jwt:SecretKey", JwtSecret);
                b.UseSetting("Jwt:Issuer", JwtIssuer);
                b.UseSetting("Jwt:Audience", JwtAudience);
                b.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:Provider"] = "PostgreSql",
                    ["ConnectionStrings:DefaultConnection"] = _pg.GetConnectionString(),
                    ["ConnectionStrings:Redis"] = "localhost:6379",
                    ["Jwt:Issuer"] = JwtIssuer,
                    ["Jwt:Audience"] = JwtAudience,
                    ["Jwt:SecretKey"] = JwtSecret,
                    ["Jwt:ExpirationMinutes"] = "60",
                    ["Anthropic:Enabled"] = "false",
                }));
            });
    }

    private static string GerarJwt(Guid empresaId)
    {
        var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret)), SecurityAlgorithms.HmacSha256);
        var claims = new List<Claim>
        {
            new("sub", UsuarioId.ToString()),
            new("nivel", "Admin"),
            new("empresaId", empresaId.ToString()),
            new("iat", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
        };
        var token = new JwtSecurityToken(JwtIssuer, JwtAudience, claims, expires: DateTime.UtcNow.AddMinutes(30), signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

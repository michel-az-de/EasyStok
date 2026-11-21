using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Services;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace EasyStock.Api.IntegrationTests.Mobile;

/// <summary>
/// #2448: as rotas de escrita da gestão mobile só exigiam um usuário autenticado. Agora cada uma exige o nível e o
/// módulo da ação web equivalente. Pela API real: o perfil sem o módulo (ou sem o nível) recebe 403 antes de o
/// controller rodar, e a Dona (Admin, todos os módulos) segue passando da autorização.
/// </summary>
[Collection("MobileE2E")]
public class EscritaGestaoMobileAutorizacaoE2ETests(MobileE2EFixture fixture)
{
    private const string Id = "id-que-nao-existe";

    // Rota e o perfil Casa da Baba que não tem o que ela exige (módulo ou nível).
    public static TheoryData<string, string> RotasEPerfilBarrado => new()
    {
        { $"/api/mobile/cash/{Id}/promover", "Cozinha" },
        { $"/api/mobile/cash/{Id}/unlink", "Cozinha" },
        { $"/api/mobile/products/{Id}/approve", "Cozinha" },
        { $"/api/mobile/products/{Id}/link", "Cozinha" },
        { $"/api/mobile/products/{Id}/unlink", "Cozinha" },
        { $"/api/mobile/products/{Id}/reconcile-stock", "Atendimento" },
        { $"/api/mobile/orders/{Id}/link", "Cozinha" },
        { $"/api/mobile/orders/{Id}/unlink", "Cozinha" },
        { "/api/mobile/orders/backfill-vendas", "Atendimento" },
        { $"/api/mobile/batches/{Id}/link", "Atendimento" },
        { $"/api/mobile/batches/{Id}/unlink", "Atendimento" },
        { $"/api/mobile/clients/{Id}/link", "Cozinha" },
        { $"/api/mobile/clients/{Id}/unlink", "Cozinha" },
        { "/api/mobile/calculadora/criar-compra", "Atendimento" },
    };

    public static TheoryData<string> Rotas()
    {
        var rotas = new TheoryData<string>();
        foreach (var linha in RotasEPerfilBarrado) rotas.Add((string)linha[0]);
        return rotas;
    }

    [SkippableTheory]
    [MemberData(nameof(RotasEPerfilBarrado))]
    public async Task Perfil_sem_modulo_ou_nivel_recebe_403(string rota, string perfil)
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (empresa, _) = await fixture.SeedEmpresaELojaAsync();
        using var cliente = await ClienteAsync(empresa, perfil);

        var resposta = await PostAsync(cliente, rota);

        resposta.StatusCode.Should().Be(HttpStatusCode.Forbidden, await resposta.Content.ReadAsStringAsync());
    }

    [SkippableTheory]
    [MemberData(nameof(Rotas))]
    public async Task Visualizador_recebe_403(string rota)
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (empresa, _) = await fixture.SeedEmpresaELojaAsync();
        using var cliente = await ClienteAsync(empresa, perfil: null);

        var resposta = await PostAsync(cliente, rota);

        resposta.StatusCode.Should().Be(HttpStatusCode.Forbidden, await resposta.Content.ReadAsStringAsync());
    }

    [SkippableTheory]
    [MemberData(nameof(Rotas))]
    public async Task Dona_passa_da_autorizacao(string rota)
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (empresa, _) = await fixture.SeedEmpresaELojaAsync();
        using var cliente = await ClienteAsync(empresa, "Dona");

        var resposta = await PostAsync(cliente, rota);

        // O registro não existe e o corpo é vazio: 404, 400 ou 200 provam que o controller rodou.
        resposta.StatusCode.Should().NotBe(HttpStatusCode.Forbidden, await resposta.Content.ReadAsStringAsync())
            .And.NotBe(HttpStatusCode.Unauthorized);
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient cliente, string rota)
    {
        var requisicao = new HttpRequestMessage(HttpMethod.Post, rota)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        // criar-compra exige a chave; nova por chamada para o cache de idempotência não responder pelo outro perfil.
        requisicao.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return cliente.SendAsync(requisicao);
    }

    /// <summary>JWT como o login emite: nível e permissões do perfil Casa da Baba; sem perfil, Visualizador.</summary>
    private async Task<HttpClient> ClienteAsync(Guid empresa, string? perfil)
    {
        Guid usuarioId;
        using (var scope = fixture.Factory!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            using var bypass = db.UseRowLevelSecurityBypass();
            var usuario = Usuario.Criar($"Teste {perfil ?? "Visualizador"}", $"gestao-mobile-{Guid.NewGuid():N}@example.test", "sem-login-por-senha");
            db.Usuarios.Add(usuario);
            await db.SaveChangesAsync();
            usuarioId = usuario.Id;
        }

        var definicao = perfil is null ? null : PerfisCasaDaBaba.Iniciais.Single(p => p.Nome == perfil);
        var claims = new List<Claim>
        {
            new("sub", usuarioId.ToString()),
            new("nivel", (definicao?.Nivel ?? NivelAcesso.Visualizador).ToString()),
            new("empresaId", empresa.ToString()),
            new("iat", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };
        if (definicao is not null)
            claims.AddRange(definicao.Permissoes.Select(p => new Claim("permissao", p.ToString())));

        var chave = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("EasyStock-Test-SuperSecretKey-Min32Chars!!"));
        var jwt = new JwtSecurityToken("EasyStock", "EasyStock", claims, expires: DateTime.UtcNow.AddMinutes(20),
            signingCredentials: new SigningCredentials(chave, SecurityAlgorithms.HmacSha256));
        var cliente = fixture.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(jwt));
        return cliente;
    }
}

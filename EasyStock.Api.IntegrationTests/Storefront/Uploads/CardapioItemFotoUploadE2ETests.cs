using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.UseCases.GerenciarUploads;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;
using DotNet.Testcontainers.Builders;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Api.IntegrationTests.Storefront.Uploads;

/// <summary>
/// E2E REAL do upload de foto de item de cardápio: gera bytes de uma imagem válida, sobe pelo
/// caminho de produção (multipart -> validação magic-number -> Skia -> IFileStorage), e PROVA que
/// o arquivo foi salvo de verdade lendo os bytes de volta do sink real (IFileStorage.DownloadAsync)
/// e conferindo a referência persistida no Postgres (CardapioItem.FotoUrl). Sobe API real
/// (WebApplicationFactory) + Postgres real (Testcontainers) + FileStorage:Provider=Local (adapter
/// REAL em disco). Anti-regressão: nenhum teste cobria o caminho ponta-a-ponta antes (o único
/// existente usava storage fake em memória).
/// </summary>
public sealed class CardapioItemFotoUploadE2ETests : IAsyncLifetime
{
    private PostgreSqlContainer? _pg;
    private bool _isAvailable;

    private const string JwtIssuer = "EasyStock";
    private const string JwtAudience = "EasyStock";
    private const string JwtSecret = "EasyStock-Test-SuperSecretKey-Min32Chars!!";

    // 1x1 PNG válido: assinatura 0x89 'PNG' 0x0D0A0x1A0x0A satisfaz o magic-number do
    // UploadSecurityValidator e o corpo é decodificável pelo Skia (convertido para webp no pipeline).
    private static readonly byte[] PngValido = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    public async Task InitializeAsync()
    {
        try
        {
            _pg = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("easystock_upload_e2e_tests")
                .WithUsername("postgres")
                .WithPassword("postgres")
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
        if (_pg is not null)
            await _pg.DisposeAsync();
    }

    private WebApplicationFactory<Program> CriarFactory()
    {
        if (_pg is null) throw new InvalidOperationException("Conteiner PostgreSQL nao disponivel.");

        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b =>
            {
                b.UseEnvironment("Development");
                b.ConfigureAppConfiguration((_, cfg) =>
                {
                    cfg.AddInMemoryCollection(new Dictionary<string, string?>
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
                    });
                });
            });
    }

    private static string GerarJwt(string nivel, Guid? empresaId = null)
    {
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret)), SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new("sub", Guid.NewGuid().ToString()),
            new("nivel", nivel),
        };
        if (empresaId.HasValue)
            claims.Add(new Claim("empresaId", empresaId.Value.ToString()));

        var token = new JwtSecurityToken(
            issuer: JwtIssuer,
            audience: JwtAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static async Task<(StorefrontEntity Storefront, Guid ItemId)> SeedAsync(
        WebApplicationFactory<Program> factory, Guid empresaId, string slug)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        using var _ = db.UseRowLevelSecurityBypass();

        db.Empresas.Add(new Empresa
        {
            Id = empresaId,
            Nome = "E2E Upload",
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow,
        });

        var sf = StorefrontEntity.Criar(empresaId, slug, "Loja Upload E2E", 0m);
        sf.Ativar();
        db.Storefronts.Add(sf);

        var item = CardapioItem.CriarAvulso(sf.Id, "Pão de Alho", 18.00m, "Acompanhamentos");
        item.TornarVisivel();
        db.CardapioItens.Add(item);

        await db.SaveChangesAsync();
        return (sf, item.Id);
    }

    private sealed record Envelope(UploadData Data);
    private sealed record UploadData(string Url, string FileName, string ContentType, long Size);

    // ── O coração: bytes reais sobem e são lidos de volta do sink ──────────

    [SkippableFact]
    public async Task POST_FotoCardapioItem_PersisteBytesNoSinkReal()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");

        await using var factory = CriarFactory();
        var empresaId = Guid.NewGuid();
        var (_, itemId) = await SeedAsync(factory, empresaId, "loja-upload");

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GerarJwt("Admin", empresaId));

        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(PngValido);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(fileContent, "file", "foto.png");

        var resp = await client.PostAsync($"/api/uploads/cardapio-item/{itemId}/foto", form);

        resp.StatusCode.Should().Be(HttpStatusCode.OK, "upload de imagem válida deve persistir");

        var env = await resp.Content.ReadFromJsonAsync<Envelope>();
        env.Should().NotBeNull();
        var url = env!.Data.Url;
        url.Should().NotBeNullOrWhiteSpace();
        env.Data.Size.Should().BeGreaterThan(0);

        // READ-BACK do sink REAL: lê os bytes de volta pela porta IFileStorage (funciona Local e S3).
        // É isto que prova que o arquivo foi salvo de verdade — não um mock dizendo "ok".
        using (var scope = factory.Services.CreateScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
            var storageKey = StorageKeyExtractor.Extract(url);
            storageKey.Should().NotBeNullOrWhiteSpace("a URL local /files/<key> deve render a chave de storage");

            (await storage.ExistsAsync(storageKey!, CancellationToken.None))
                .Should().BeTrue("o arquivo precisa existir fisicamente no storage");

            var bytes = await storage.DownloadAsync(storageKey!, CancellationToken.None);
            bytes.Should().NotBeNullOrEmpty("os bytes lidos de volta provam a persistência real");
            bytes.Length.Should().Be((int)env.Data.Size, "o tamanho lido bate com o reportado pelo upload");

            // Quando o Skia decodifica a imagem, o pipeline a converte para webp (RIFF....WEBP).
            if (string.Equals(env.Data.ContentType, "image/webp", StringComparison.OrdinalIgnoreCase))
            {
                bytes.Take(4).Should().Equal(new byte[] { 0x52, 0x49, 0x46, 0x46 }, "header RIFF do container webp");
                bytes.Skip(8).Take(4).Should().Equal(new byte[] { 0x57, 0x45, 0x42, 0x50 }, "marca WEBP no offset 8");
            }
        }

        // Metadata persistida no Postgres: CardapioItem.FotoUrl aponta para o arquivo salvo.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            using var _ = db.UseRowLevelSecurityBypass();
            var item = await db.CardapioItens.AsNoTracking().FirstOrDefaultAsync(c => c.Id == itemId);
            item.Should().NotBeNull();
            item!.FotoUrl.Should().Be(url, "a referência da foto deve ficar persistida no item");
        }
    }

    // ── Anti-regressão dos gates de segurança ──────────────────────────────

    [SkippableFact]
    public async Task POST_FotoCardapioItem_ArquivoRenomeado_Returns400()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");

        await using var factory = CriarFactory();
        var empresaId = Guid.NewGuid();
        var (_, itemId) = await SeedAsync(factory, empresaId, "loja-upload-fake");

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GerarJwt("Admin", empresaId));

        // Bytes de texto, mas declarados como image/png: o magic-number deve barrar (anti XSS armazenado).
        using var form = new MultipartFormDataContent();
        var fake = new ByteArrayContent(Encoding.UTF8.GetBytes("isto nao e uma imagem"));
        fake.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(fake, "file", "fake.png");

        var resp = await client.PostAsync($"/api/uploads/cardapio-item/{itemId}/foto", form);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "assinatura de bytes diferente do content-type declarado deve ser rejeitada (400)");
    }

    [SkippableFact]
    public async Task POST_FotoCardapioItem_SemAuth_Returns401()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");

        await using var factory = CriarFactory();
        var empresaId = Guid.NewGuid();
        var (_, itemId) = await SeedAsync(factory, empresaId, "loja-upload-noauth");

        using var client = factory.CreateClient(); // sem header Authorization

        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(PngValido);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(fileContent, "file", "foto.png");

        var resp = await client.PostAsync($"/api/uploads/cardapio-item/{itemId}/foto", form);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "endpoint de upload exige autenticação");
    }
}

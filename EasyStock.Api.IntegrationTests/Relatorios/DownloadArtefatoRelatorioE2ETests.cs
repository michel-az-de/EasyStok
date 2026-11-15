using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using EasyStock.Api.IntegrationTests.Mobile;
using EasyStock.Api.Services.Relatorios;
using EasyStock.Application.Ports.Output.Reporting;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Reporting;
using EasyStock.Infra.Async.Storage;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace EasyStock.Api.IntegrationTests.Relatorios;

/// <summary>
/// #2668: o link de download que o GET da run devolvia apontava para <c>/api/files/local-signed/...</c>, rota que
/// não existe, e o "token" era só base64 da chave, sem assinatura. Postgres real, storage local real, HTTP real.
/// </summary>
[Trait("Category", "SecurityRegression")]
public sealed class DownloadArtefatoRelatorioE2ETests(MobileE2EFixture fx) : IClassFixture<MobileE2EFixture>
{
    private const string Conteudo = "data;total\n2026-11-15;42\n";

    [SkippableFact]
    public async Task Link_do_GET_da_run_baixa_o_artefato_com_cabecalhos_de_download()
    {
        Skip.IfNot(fx.IsAvailable, "Docker indisponível");
        var (empresa, usuario) = await SeedEmpresaEUsuarioAsync();
        var run = await SeedRunConcluidaAsync(empresa, usuario);

        var api = fx.CreateClient();
        api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Jwt(empresa, usuario));
        var detalhe = await api.GetAsync($"/api/reports/runs/{run.Id}");
        var corpo = await detalhe.Content.ReadAsStringAsync();
        detalhe.StatusCode.Should().Be(HttpStatusCode.OK, corpo);
        var link = JsonDocument.Parse(corpo).RootElement.GetProperty("data").GetProperty("downloadUrl").GetString();
        link.Should().NotBeNullOrEmpty(corpo);

        // Aba nova do Web: sem JWT.
        var resposta = await fx.CreateClient().GetAsync(link);

        resposta.StatusCode.Should().Be(HttpStatusCode.OK, $"o link {link} tem de baixar o artefato");
        (await resposta.Content.ReadAsStringAsync()).Should().Be(Conteudo);
        resposta.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        resposta.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        resposta.Content.Headers.ContentDisposition.FileName.Should().Be($"vendas-por-periodo_{run.Id:N}.csv");
        resposta.Headers.CacheControl!.NoStore.Should().BeTrue();
        resposta.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
    }

    [SkippableFact]
    public async Task Token_adulterado_da_404()
    {
        Skip.IfNot(fx.IsAvailable, "Docker indisponível");
        var (empresa, usuario) = await SeedEmpresaEUsuarioAsync();
        var run = await SeedRunConcluidaAsync(empresa, usuario);
        var link = (await Link().CriarAsync(run, "x.csv", TimeSpan.FromMinutes(15), default)).ToString();
        var trocado = link[^3] == 'A' ? 'B' : 'A';
        var adulterado = link[..^3] + trocado + link[^2..];

        (await fx.CreateClient().GetAsync(adulterado)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await fx.CreateClient().GetAsync(RelatorioDownloadLink.Rota + "/nao-e-token")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [SkippableFact]
    public async Task Token_vencido_da_404()
    {
        Skip.IfNot(fx.IsAvailable, "Docker indisponível");
        var (empresa, usuario) = await SeedEmpresaEUsuarioAsync();
        var run = await SeedRunConcluidaAsync(empresa, usuario);
        var link = await Link().CriarAsync(run, "x.csv", TimeSpan.FromMinutes(-1), default);

        (await fx.CreateClient().GetAsync(link.ToString())).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [SkippableFact]
    public async Task Token_assinado_para_outra_empresa_nao_abre_a_run_desta()
    {
        Skip.IfNot(fx.IsAvailable, "Docker indisponível");
        var (empresa, usuario) = await SeedEmpresaEUsuarioAsync();
        var run = await SeedRunConcluidaAsync(empresa, usuario);
        var (outra, _) = await SeedEmpresaEUsuarioAsync();
        var token = Protetor().Protect($"{outra:N}|{run.Id:N}|{run.ArtifactStorageKey}", DateTimeOffset.UtcNow.AddMinutes(15));

        (await fx.CreateClient().GetAsync($"{RelatorioDownloadLink.Rota}/{token}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [SkippableFact]
    public async Task Chave_fora_da_raiz_do_storage_e_recusada()
    {
        Skip.IfNot(fx.IsAvailable, "Docker indisponível");
        var (empresa, usuario) = await SeedEmpresaEUsuarioAsync();
        var raiz = Path.GetFullPath(((LocalFileStorage)fx.Factory!.Services.GetRequiredService<IFileStorage>()).GetRootPath());
        var fora = Directory.CreateTempSubdirectory("fora-2668-").FullName;
        var segredo = Path.Combine(fora, "segredo.csv");
        await File.WriteAllTextAsync(segredo, "segredo");
        // Chave com "..": aponta para um arquivo que existe, mas fora da raiz do storage.
        var chave = Path.GetRelativePath(raiz, segredo).Replace('\\', '/');
        var run = await SeedRunAsync(empresa, usuario, chave);
        var link = await Link().CriarAsync(run, "x.csv", TimeSpan.FromMinutes(15), default);

        var resposta = await fx.CreateClient().GetAsync(link.ToString());

        resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await resposta.Content.ReadAsStringAsync()).Should().NotContain("segredo");
    }

    // ─── Apoio ──────────────────────────────────────────────────────────────

    private IRelatorioDownloadLink Link() => fx.Factory!.Services.GetRequiredService<IRelatorioDownloadLink>();

    private ITimeLimitedDataProtector Protetor() => fx.Factory!.Services.GetRequiredService<IDataProtectionProvider>()
        .CreateProtector(RelatorioDownloadLink.Proposito).ToTimeLimitedDataProtector();

    private async Task<(Guid Empresa, Guid Usuario)> SeedEmpresaEUsuarioAsync()
    {
        var (empresa, _) = await fx.SeedEmpresaELojaAsync();
        using var scope = fx.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        using var bypass = db.UseRowLevelSecurityBypass();
        var usuario = Usuario.Criar("Relatorios 2668", $"r2668-{Guid.NewGuid():N}@example.test", "sem-login-por-senha");
        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync();
        return (empresa, usuario.Id);
    }

    private async Task<ReportRun> SeedRunConcluidaAsync(Guid empresa, Guid usuario)
    {
        var chave = $"tenants/{empresa}/reports/2026/11/{Guid.NewGuid():N}.csv";
        var storage = fx.Factory!.Services.GetRequiredService<IFileStorage>();
        await using (var escrita = await storage.OpenUploadStreamAsync(chave, "text/csv", default))
            await escrita.WriteAsync(Encoding.UTF8.GetBytes(Conteudo));
        return await SeedRunAsync(empresa, usuario, chave);
    }

    private async Task<ReportRun> SeedRunAsync(Guid empresa, Guid usuario, string chave)
    {
        var run = ReportRun.Enqueue(empresa, usuario, "vendas.por-periodo", ReportCategoria.Vendas, ReportContexto.Tenant,
            "{}", "hash", ReportFormat.Csv, "1.0.0", TimeSpan.FromDays(7));
        run.TryStart(TimeSpan.FromMinutes(5));
        run.MarkSucceeded(chave, Conteudo.Length, "sha", 1);
        using var scope = fx.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        db.Add(run);
        await db.SaveChangesAsync();
        return run;
    }

    private static string Jwt(Guid empresa, Guid usuario)
    {
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes("EasyStock-Test-SuperSecretKey-Min32Chars!!")), SecurityAlgorithms.HmacSha256);
        var claims = new List<Claim>
        {
            new("sub", usuario.ToString()),
            new("nivel", "Admin"),
            new("empresaId", empresa.ToString()),
            new("iat", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
        };
        var token = new JwtSecurityToken("EasyStock", "EasyStock", claims, expires: DateTime.UtcNow.AddMinutes(30), signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

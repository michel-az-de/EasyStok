using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Api.IntegrationTests.Mobile;

/// <summary>
/// #2180 de ponta a ponta: a API roda com login sem superusuário nem BYPASSRLS e as tabelas
/// <c>mobile_*</c> têm <c>tenant_isolation</c>. O aparelho autentica (lookup antes do tenant), envia e
/// puxa pelo sync, e o aparelho de outra empresa não recebe nada dele.
/// </summary>
[Collection("MobileE2E")]
public class MobileRlsE2ETests(MobileE2EFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [SkippableFact]
    public async Task Api_roda_sujeita_a_RLS()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        using var scope = fixture.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        using var semRls = db.UseRowLevelSecurityBypass(); // teste lê e semeia entre empresas

        var privilegiado = await db.Database.SqlQueryRaw<bool>(
            "SELECT rolsuper OR rolbypassrls AS \"Value\" FROM pg_roles WHERE rolname = current_user").SingleAsync();
        privilegiado.Should().BeFalse("sem isso a policy não vale e o E2E não prova o APK sob RLS");
    }

    [SkippableFact]
    public async Task Sync_do_aparelho_funciona_e_nao_cruza_empresas()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (empresaA, lojaA) = await fixture.SeedEmpresaELojaAsync();
        var (empresaB, lojaB) = await fixture.SeedEmpresaELojaAsync();
        var a = await fixture.SeedMobileDeviceAsync(empresaA, lojaA);
        var outroDeA = await fixture.SeedMobileDeviceAsync(empresaA, lojaA);
        var b = await fixture.SeedMobileDeviceAsync(empresaB, lojaB);
        var produto = $"p-{Guid.NewGuid():N}"[..24];

        var push = await fixture.CreateMobileClient(a.ApiKey).PostAsJsonAsync("/api/mobile/sync", new
        {
            deviceId = a.DeviceId,
            operatorName = "Thati",
            mutations = new[]
            {
                new
                {
                    id = "mut_" + Guid.NewGuid(), deviceId = a.DeviceId, type = "product.upsert",
                    payload = JsonSerializer.SerializeToElement(
                        new { id = produto, name = "Lasanha", category = "massa", unit = "300g", price = 30m, stock = 4, custom = true }, Json),
                    ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                },
            },
        }, Json);
        push.StatusCode.Should().Be(HttpStatusCode.OK, await push.Content.ReadAsStringAsync());
        var corpo = await push.Content.ReadFromJsonAsync<JsonElement>();
        corpo.GetProperty("acceptedIds").GetArrayLength().Should().Be(1, corpo.ToString());

        // O pull não devolve ao aparelho o que ele mesmo enviou; o outro aparelho da empresa recebe.
        (await PullIdsAsync(outroDeA)).Should().Contain(produto);
        (await PullIdsAsync(b)).Should().NotContain(produto, "o aparelho da outra empresa não enxerga a linha");
    }

    /// <summary>
    /// MutationId é global (índice único). Sob a RLS a checagem de "já processada" só via a empresa do
    /// aparelho: a mutation gravada por outra empresa era reaplicada e o lote batia no índice em toda
    /// nova tentativa. O reenvio precisa responder sem erro e sem gravar nada na segunda empresa.
    /// </summary>
    [SkippableFact]
    public async Task Mutation_ja_processada_em_outra_empresa_nao_trava_o_lote()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (empresaA, lojaA) = await fixture.SeedEmpresaELojaAsync();
        var (empresaB, lojaB) = await fixture.SeedEmpresaELojaAsync();
        var a = await fixture.SeedMobileDeviceAsync(empresaA, lojaA);
        var b = await fixture.SeedMobileDeviceAsync(empresaB, lojaB);
        var outroDeB = await fixture.SeedMobileDeviceAsync(empresaB, lojaB);
        var mutation = "mut_" + Guid.NewGuid();
        var produto = $"p-{Guid.NewGuid():N}"[..24];

        Task<HttpResponseMessage> Enviar(MobileDeviceCredentials de) => fixture.CreateMobileClient(de.ApiKey).PostAsJsonAsync("/api/mobile/sync", new
        {
            deviceId = de.DeviceId,
            operatorName = "Thati",
            mutations = new[]
            {
                new
                {
                    id = mutation, deviceId = de.DeviceId, type = "product.upsert",
                    payload = JsonSerializer.SerializeToElement(
                        new { id = produto, name = "Lasanha", category = "massa", unit = "300g", price = 30m, stock = 4, custom = true }, Json),
                    ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                },
            },
        }, Json);

        (await Enviar(a)).StatusCode.Should().Be(HttpStatusCode.OK);
        for (var tentativa = 1; tentativa <= 2; tentativa++)
        {
            var reenvio = await Enviar(b);
            reenvio.StatusCode.Should().Be(HttpStatusCode.OK, $"tentativa {tentativa}: {await reenvio.Content.ReadAsStringAsync()}");
        }

        (await PullIdsAsync(outroDeB)).Should().NotContain(produto, "a mutation de outra empresa não vira dado da segunda");
        await ComoSuporteAsync(async db =>
            (await db.MobileProcessedMutations.IgnoreQueryFilters().CountAsync(m => m.MutationId == mutation)).Should().Be(1));
    }

    [SkippableFact]
    public async Task Api_key_desconhecida_continua_401()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var resp = await fixture.CreateMobileClient("tk-inexistente").GetAsync("/api/mobile/sync/pull?since=0&deviceId=x");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Aparelho com sessão nativa (login pelo app, #2184): o filtro da API key e o SSE revalidam a
    /// família e o aparelho antes de existir tenant. Sob RLS essa revalidação não pode cair em 401 nem
    /// deixar passar a sessão revogada.
    /// </summary>
    [SkippableFact]
    public async Task Aparelho_com_sessao_nativa_sincroniza_abre_o_SSE_e_cai_quando_a_sessao_e_revogada()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (empresa, loja) = await fixture.SeedEmpresaELojaAsync();
        var aparelho = await fixture.SeedMobileDeviceAsync(empresa, loja);
        var sessao = Guid.NewGuid();
        await ComoSuporteAsync(async db =>
        {
            var usuario = Usuario.Criar("Ana", $"ana-{Guid.NewGuid():N}@rls.test", "hash");
            usuario.Empresas.Add(new UsuarioEmpresa { UsuarioId = usuario.Id, EmpresaId = empresa, Ativo = true });
            db.AddRange(usuario, new SessaoNativa
            {
                Id = sessao, UsuarioId = usuario.Id, EmpresaId = empresa, DispositivoId = aparelho.DeviceId,
                CriadaEm = DateTime.UtcNow.AddMinutes(-1),
            });
            await db.SaveChangesAsync();
            await db.Set<MobileDevice>().Where(d => d.Id == aparelho.DeviceId)
                .ExecuteUpdateAsync(u => u.SetProperty(d => d.SessaoNativaId, sessao));
        });

        (await PullIdsAsync(aparelho)).Should().BeEmpty();
        (await AbrirSseAsync(aparelho)).Should().Be(HttpStatusCode.OK, "o SSE da sessão nativa revalida o aparelho sob RLS");

        await ComoSuporteAsync(async db => (await db.Set<SessaoNativa>().IgnoreQueryFilters().Where(s => s.Id == sessao)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.RevogadaEm, DateTime.UtcNow))).Should().Be(1));

        (await fixture.CreateMobileClient(aparelho.ApiKey).GetAsync($"/api/mobile/sync/pull?since=0&deviceId={aparelho.DeviceId}"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized, "sessão revogada derruba a API key nativa");
        (await AbrirSseAsync(aparelho)).Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<HttpStatusCode> AbrirSseAsync(MobileDeviceCredentials device)
    {
        using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var http = fixture.CreateClient();
        using var resp = await http.GetAsync($"/api/mobile/operation/stream?apiKey={device.ApiKey}",
            HttpCompletionOption.ResponseHeadersRead, limite.Token);
        return resp.StatusCode;
    }

    private async Task ComoSuporteAsync(Func<EasyStockDbContext, Task> acao)
    {
        using var scope = fixture.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        using var semRls = db.UseRowLevelSecurityBypass(); // teste semeia entre empresas
        await acao(db);
    }

    private async Task<List<string>> PullIdsAsync(MobileDeviceCredentials device)
    {
        var resp = await fixture.CreateMobileClient(device.ApiKey)
            .GetAsync($"/api/mobile/sync/pull?since=0&deviceId={device.DeviceId}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
        var corpo = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return corpo.GetProperty("mutations").EnumerateArray()
            .Select(m => m.GetProperty("payload").GetProperty("id").GetString()!)
            .ToList();
    }
}

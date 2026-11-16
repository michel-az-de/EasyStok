using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EasyStock.Application.UseCases.EstornarMovimentoCaixa;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Api.IntegrationTests.Mobile;

/// <summary>
/// Estorno feito no Caixa do console depois do cursor do aparelho. O lançamento nasceu antes do
/// cursor (no console ou no aparelho) e o estorno não descia: o pull reverso filtrava só por
/// <c>CriadoEm</c> e deixava de fora as referências <c>mobile:</c>.
/// </summary>
[Collection("MobileE2E")]
public class SyncEstornoConsoleE2ETests(MobileE2EFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [SkippableFact]
    public async Task LancamentoDoConsole_EstornadoNoConsole_ChegaEstornadoAosAparelhos()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (empresaId, lojaId, a, b) = await DoisAparelhosAsync();

        // Saída lançada no Caixa do console ontem: os aparelhos já a receberam.
        var movimento = new MovimentoCaixa
        {
            Id = Guid.NewGuid(), EmpresaId = empresaId, LojaId = lojaId, Tipo = "saida", Valor = 12m,
            Descricao = "Gás", Metodo = "pix", DataMovimento = DateTime.UtcNow, Origem = "web",
        };
        using (var scope = fixture.Factory!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.Add(movimento);
            await db.SaveChangesAsync();
        }
        await LancadoOntemAsync(movimento.Id);
        await PullAsync(a);
        await PullAsync(b);

        await EstornarNoConsoleAsync(empresaId, movimento.Id);

        foreach (var aparelho in new[] { a, b })
            (await PullAsync(aparelho)).Should().ContainSingle(m => m.Tipo == "cashEntry.upsert" && m.Id == movimento.Id.ToString(),
                    "o estorno do console precisa chegar ao aparelho {0}", aparelho.DeviceId)
                .Which.Payload.GetProperty("estornado").GetBoolean().Should().BeTrue();
    }

    [SkippableFact]
    public async Task LancamentoDoAparelho_EstornadoNoConsole_ChegaEstornadoAosAparelhos()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (empresaId, _, a, b) = await DoisAparelhosAsync();

        // A lança a saída; o vínculo cria o MovimentoCaixa "mobile:<id>" dentro do push.
        var lancamento = "ce-" + Guid.NewGuid().ToString("N")[..20];
        var envio = new
        {
            id = "mut_" + Guid.NewGuid(), deviceId = a.DeviceId, type = "cashEntry.upsert",
            payload = new { id = lancamento, type = "expense", amount = 15m, description = "Gás", metodo = "pix", createdAt = Ms(DateTime.UtcNow) },
            ts = Ms(DateTime.UtcNow),
        };
        (await a.Http.PostAsJsonAsync("/api/mobile/sync", new { deviceId = a.DeviceId, operatorName = "Thati", mutations = new[] { envio } }, Json))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var movimentoId = await LerAsync(db => db.Set<MovimentoCaixa>().IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.Referencia == "mobile:" + lancamento).Select(m => m.Id).SingleAsync());
        await LancadoOntemAsync(movimentoId, lancamento);
        await PullAsync(a);
        await PullAsync(b);

        await EstornarNoConsoleAsync(empresaId, movimentoId);

        foreach (var aparelho in new[] { a, b })
            (await PullAsync(aparelho)).Should().ContainSingle(m => m.Tipo == "cashEntry.upsert" && m.Id == lancamento,
                    "o estorno do console precisa chegar ao aparelho {0}", aparelho.DeviceId)
                .Which.Payload.GetProperty("estornado").GetBoolean().Should().BeTrue();
    }

    // ---------- apoio ----------

    private sealed record Aparelho(string DeviceId, HttpClient Http)
    {
        public long Cursor { get; set; }
    }

    private sealed record Recebida(string Tipo, string Id, JsonElement Payload);

    private async Task<(Guid EmpresaId, Guid LojaId, Aparelho A, Aparelho B)> DoisAparelhosAsync()
    {
        var (empresaId, lojaId) = await fixture.SeedEmpresaELojaAsync();
        async Task<Aparelho> NovoAsync()
        {
            var cred = await fixture.SeedMobileDeviceAsync(empresaId, lojaId);
            return new Aparelho(cred.DeviceId, fixture.CreateMobileClient(cred.ApiKey));
        }
        return (empresaId, lojaId, await NovoAsync(), await NovoAsync());
    }

    /// <summary>Recua o lançamento para ontem: fora da sobreposição de 30 s do cursor (#2477).</summary>
    private async Task LancadoOntemAsync(Guid movimentoId, string? lancamentoDoAparelho = null)
    {
        var ontem = DateTime.UtcNow.AddDays(-1);
        using var scope = fixture.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        await db.Set<MovimentoCaixa>().IgnoreQueryFilters().Where(m => m.Id == movimentoId)
            .ExecuteUpdateAsync(u => u.SetProperty(m => m.CriadoEm, ontem));
        if (lancamentoDoAparelho is not null)
            await db.Set<CashEntry>().IgnoreQueryFilters().Where(c => c.Id == lancamentoDoAparelho)
                .ExecuteUpdateAsync(u => u.SetProperty(c => c.ServerUpdatedAt, ontem));
    }

    /// <summary>O mesmo use case do botão Estornar do Caixa no console.</summary>
    private async Task EstornarNoConsoleAsync(Guid empresaId, Guid movimentoId)
    {
        using var scope = fixture.Factory!.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<EasyStockDbContext>().SetMobileTenantContext(empresaId);
        var estornado = await scope.ServiceProvider.GetRequiredService<EstornarMovimentoCaixaUseCase>()
            .ExecuteAsync(new EstornarMovimentoCaixaCommand(empresaId, movimentoId, "lançado errado", null, "Felipe"));
        estornado.Should().NotBeNull();
    }

    private static async Task<List<Recebida>> PullAsync(Aparelho aparelho)
    {
        var resp = await aparelho.Http.GetAsync($"/api/mobile/sync/pull?since={aparelho.Cursor}&deviceId={aparelho.DeviceId}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
        var corpo = await resp.Content.ReadFromJsonAsync<JsonElement>();
        aparelho.Cursor = corpo.GetProperty("serverTime").GetInt64();
        return corpo.GetProperty("mutations").EnumerateArray()
            .Select(m => new Recebida(m.GetProperty("type").GetString()!,
                m.GetProperty("payload").GetProperty("id").GetString()!, m.GetProperty("payload").Clone()))
            .ToList();
    }

    private async Task<T> LerAsync<T>(Func<EasyStockDbContext, Task<T>> consulta)
    {
        using var scope = fixture.Factory!.Services.CreateScope();
        return await consulta(scope.ServiceProvider.GetRequiredService<EasyStockDbContext>());
    }

    private static long Ms(DateTime utc) => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
}

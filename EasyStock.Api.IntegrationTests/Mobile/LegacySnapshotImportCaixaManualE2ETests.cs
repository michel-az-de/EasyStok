using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EasyStock.Api.Mobile.DTOs;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Api.IntegrationTests.Mobile;

/// <summary>
/// #2813, opção B do dono do produto: o lançamento manual de caixa do histórico fica só como arquivo em mobile_cash_entries, com a
/// marca <c>NaoPromoverEm</c> (data ORIGINAL do lançamento), e NÃO vira movimento do caixa do ERP, nem pelo import nem pelo
/// backfill nem pelo push. O lançamento comum, que não veio do import, continua sendo promovido. Dado sintético.
/// </summary>
[Collection("MobileE2E")]
public class LegacySnapshotImportCaixaManualE2ETests(MobileE2EFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private sealed record Cenario(MobileDeviceCredentials Device, HttpClient Http, LegacySnapshotRequest Request, string[] Ids, long[] Quando);

    [SkippableFact]
    public async Task Import_2EntradasE1Saida_GravaSoArquivoMarcadoComDataOriginal_ESemMovimentoNoCaixa()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var c = await CenarioAsync(("income", 10m, false), ("income", 20.5m, false), ("expense", 7.25m, false));

        var recibo = await ImportarAsync(c);

        recibo.Counts["cashEntries"].Should().Be(3);
        (await MovimentosAsync(c.Device.EmpresaId)).Should().Be(0, "o lançamento manual do histórico fica só como arquivo");
        var linhas = await LancamentosAsync(c.Device.EmpresaId);
        linhas.Count(x => x.Type == "income").Should().Be(2);
        linhas.Count(x => x.Type == "expense").Should().Be(1);
        DevemEstarArquivados(linhas, c);
    }

    [SkippableFact]
    public async Task Backfill_PeloPontoDeEntradaDoAparelho_NaoPromoveOArquivoDoHistorico()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var c = await CenarioAsync(("income", 10m, false), ("income", 20.5m, false), ("expense", 7.25m, false));
        await ImportarAsync(c);

        var resposta = await BackfillAsync(c);

        resposta.GetProperty("processed").GetProperty("cashEntries").GetInt32().Should().Be(0, "o arquivo do histórico não é pendência de promoção");
        (await MovimentosAsync(c.Device.EmpresaId)).Should().Be(0);
        DevemEstarArquivados(await LancamentosAsync(c.Device.EmpresaId), c);
    }

    [SkippableFact]
    public async Task LancamentoComumNaoImportado_SegueSendoPromovidoPeloPushEPeloBackfill()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var c = await CenarioAsync(("income", 10m, false), ("expense", 7.25m, false));
        var empresa = c.Device.EmpresaId;
        await ImportarAsync(c);

        var doPush = "cash-" + Guid.NewGuid().ToString("N");
        await PushAsync(c, "cashEntry.upsert", new { id = doPush, type = "expense", amount = 12.34m, description = "Compra comum sintetica",
            createdAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
        (await MovimentosDaReferenciaAsync(empresa, "mobile:" + doPush)).Should().Be(1, "o lançamento comum continua indo ao caixa pelo push");

        var pendente = "cash-" + Guid.NewGuid().ToString("N");
        await LerAsync(empresa, async db =>
        {
            db.Add(new CashEntry { Id = pendente, Type = "income", Amount = 5m, Description = "Entrada comum sintetica",
                CreatedAt = DateTime.UtcNow.AddHours(-1), EmpresaId = empresa, LojaId = c.Device.LojaId });
            await db.SaveChangesAsync();
            return 0;
        });
        var resposta = await BackfillAsync(c);

        resposta.GetProperty("processed").GetProperty("cashEntries").GetInt32().Should().Be(1, "só o comum pendente conta");
        (await MovimentosDaReferenciaAsync(empresa, "mobile:" + pendente)).Should().Be(1, "o lançamento comum pendente é promovido pelo backfill");
        (await MovimentosAsync(empresa)).Should().Be(2, "o arquivo do histórico segue de fora");
        var linhas = await LancamentosAsync(empresa);
        DevemEstarArquivados(linhas, c);
        linhas.Where(x => !c.Ids.Contains(x.Id)).Should().OnlyContain(x => x.NaoPromoverEm == null && x.ErpMovimentoCaixaId != null);
    }

    [SkippableFact]
    public async Task ReimportMesmoImportId_EIdempotente_SemNovoLancamentoNemMovimento()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var c = await CenarioAsync(("income", 10m, false), ("expense", 7.25m, false));
        var empresa = c.Device.EmpresaId;
        await ImportarAsync(c);
        var antes = (await LancamentosAsync(empresa)).Select(x => (x.Id, x.CreatedAt, x.NaoPromoverEm, x.ServerUpdatedAt)).ToList();

        var repetido = await ImportarAsync(c);

        repetido.Replayed.Should().BeTrue();
        (await MovimentosAsync(empresa)).Should().Be(0);
        var depois = (await LancamentosAsync(empresa)).Select(x => (x.Id, x.CreatedAt, x.NaoPromoverEm, x.ServerUpdatedAt)).ToList();
        depois.Should().Equal(antes, "o reenvio do mesmo ImportId não grava nada de novo");
    }

    [SkippableFact]
    public async Task LancamentoEstornadoNoSnapshot_FicaArquivadoMarcado_SemMovimentoNoCaixa()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var c = await CenarioAsync(("expense", 9m, true), ("income", 3m, false));

        await ImportarAsync(c);

        (await MovimentosAsync(c.Device.EmpresaId)).Should().Be(0, "nem o estornado vira movimento: o estado segue no backup original");
        DevemEstarArquivados(await LancamentosAsync(c.Device.EmpresaId), c);
    }

    [SkippableFact]
    public async Task EdicaoDoArquivoNoAparelho_NaoPromoveNemLimpaAMarca()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var c = await CenarioAsync(("income", 10m, false), ("expense", 7.25m, false));
        var empresa = c.Device.EmpresaId;
        await ImportarAsync(c);

        await PushAsync(c, "cashEntry.upsert", new { id = c.Ids[0], type = "income", amount = 11m,
            description = "Lancamento sintetico corrigido no aparelho", createdAt = c.Quando[0] });

        var linhas = await LancamentosAsync(empresa);
        linhas.Single(x => x.Id == c.Ids[0]).Description.Should().Contain("corrigido", "a edição do operador continua valendo no arquivo");
        (await MovimentosAsync(empresa)).Should().Be(0, "editar o arquivo no aparelho não o promove ao caixa");
        DevemEstarArquivados(linhas, c);
    }

    private static void DevemEstarArquivados(List<CashEntry> linhas, Cenario c)
    {
        var arquivados = linhas.Where(x => c.Ids.Contains(x.Id)).ToList();
        arquivados.Should().HaveCount(c.Ids.Length);
        for (var i = 0; i < c.Ids.Length; i++)
        {
            var linha = arquivados.Single(x => x.Id == c.Ids[i]);
            var original = DateTimeOffset.FromUnixTimeMilliseconds(c.Quando[i]).UtcDateTime;
            linha.CreatedAt.Should().Be(original);
            linha.NaoPromoverEm.Should().Be(original, "a marca guarda a data ORIGINAL do lançamento, nunca a de hoje");
            linha.ErpMovimentoCaixaId.Should().BeNull();
        }
    }

    private async Task<Cenario> CenarioAsync(params (string Tipo, decimal Valor, bool Estornado)[] lancamentos)
    {
        var (empresa, loja) = await fixture.SeedEmpresaELojaAsync();
        var device = await fixture.SeedMobileDeviceAsync(empresa, loja);
        var primeiro = DateTimeOffset.UtcNow.AddDays(-45).ToUnixTimeMilliseconds();
        var ids = lancamentos.Select(_ => "cash-" + Guid.NewGuid().ToString("N")).ToArray();
        var quando = lancamentos.Select((_, i) => primeiro + i * 86_400_000L).ToArray();
        var linhas = lancamentos.Select((l, i) =>
        {
            var linha = new Dictionary<string, object> { ["id"] = ids[i], ["type"] = l.Tipo, ["amount"] = l.Valor,
                ["description"] = "Lancamento sintetico " + i, ["createdAt"] = quando[i] };
            if (l.Estornado) linha["estornado"] = true;
            return linha;
        }).ToArray();
        var json = JsonSerializer.Serialize(new { products = Array.Empty<object>(), clients = Array.Empty<object>(), orders = Array.Empty<object>(),
            batches = Array.Empty<object>(), cashEntries = linhas, cashClosings = Array.Empty<object>() }, Json);
        return new Cenario(device, fixture.CreateMobileClient(device.ApiKey),
            new LegacySnapshotRequest(Guid.NewGuid(), json, Sha(json), ["mut_" + Guid.NewGuid()], true, empresa, loja, "Operacao"), ids, quando);
    }

    private static async Task<LegacySnapshotResponse> ImportarAsync(Cenario c)
    {
        var response = await c.Http.PostAsJsonAsync("/api/mobile/legacy-import", c.Request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<LegacySnapshotResponse>())!;
    }

    private static async Task<JsonElement> BackfillAsync(Cenario c)
    {
        var response = await c.Http.PostAsync("/api/mobile/sync/backfill-erp-link", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task PushAsync(Cenario c, string type, object payload)
    {
        var response = await c.Http.PostAsJsonAsync("/api/mobile/sync", new { deviceId = c.Device.DeviceId,
            mutations = new[] { new { id = "mut_" + Guid.NewGuid(), deviceId = c.Device.DeviceId, type, payload, ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() } } });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        (result.TryGetProperty("rejected", out var rejected) && rejected.ValueKind == JsonValueKind.Array ? rejected.GetArrayLength() : 0).Should().Be(0, result.ToString());
    }

    private Task<int> MovimentosAsync(Guid empresa) =>
        LerAsync(empresa, db => db.Set<MovimentoCaixa>().IgnoreQueryFilters().CountAsync(m => m.EmpresaId == empresa));

    private Task<int> MovimentosDaReferenciaAsync(Guid empresa, string referencia) =>
        LerAsync(empresa, db => db.Set<MovimentoCaixa>().IgnoreQueryFilters().CountAsync(m => m.EmpresaId == empresa && m.Referencia == referencia));

    private Task<List<CashEntry>> LancamentosAsync(Guid empresa) =>
        LerAsync(empresa, db => db.Set<CashEntry>().IgnoreQueryFilters().AsNoTracking().Where(x => x.EmpresaId == empresa).OrderBy(x => x.CreatedAt).ToListAsync());

    private async Task<T> LerAsync<T>(Guid empresa, Func<EasyStockDbContext, Task<T>> acao)
    {
        using var scope = fixture.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        db.SetMobileTenantContext(empresa);
        return await acao(db);
    }

    private static string Sha(string json) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
}

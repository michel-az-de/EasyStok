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
/// #2209 (F4 da impressão v2): o número curto do lote (#2199) chega ao aparelho para entrar na chave de 12 dígitos do
/// código de barras. O aparelho que lança o lote o recebe na resposta do push (o pull nunca devolve a ele o próprio
/// lote); os outros, no <c>numeroCurto</c> do lote no pull. O número é sempre o do servidor: o que o aparelho mandar é
/// ignorado. Lote antigo, sem número, ganha um quando passa de novo pela promoção ou pelo pull.
/// </summary>
[Collection("MobileE2E")]
public class NumeroCurtoDoLoteE2ETests(MobileE2EFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [SkippableFact]
    public async Task Push_DevolveAoAparelhoQueLancouONumeroDoLoteCriado()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (a, _) = await DoisAparelhosAsync();
        var lote = NovoId("b");

        var resposta = await PushAsync(a, Cadastro(Produto), Lote(lote));

        resposta.Rejeitadas.Should().BeEmpty();
        var noBanco = await NumeroNoBancoAsync(lote);
        noBanco.Should().MatchRegex("^[0-9]{8}$", "o trigger dá o número na criação do lote");
        resposta.Numeros.Should().Contain(lote, noBanco);
    }

    [SkippableFact]
    public async Task Push_RespostaPerdida_OReenvioDaMesmaMutacaoDevolveONumero()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (a, _) = await DoisAparelhosAsync();
        var lote = NovoId("b");
        var envio = Lote(lote);
        (await PushAsync(a, Cadastro(Produto), envio)).Rejeitadas.Should().BeEmpty();

        // O aparelho não viu a resposta e reenvia a mesma mutação: o servidor já a processou e só confirma.
        var reenvio = await PushAsync(a, envio);

        reenvio.Aceitas.Should().Contain(envio.Id);
        var noBanco = await NumeroNoBancoAsync(lote);
        noBanco.Should().MatchRegex("^[0-9]{8}$");
        reenvio.Numeros.Should().Contain(lote, noBanco);
    }

    [SkippableFact]
    public async Task Pull_OutroAparelhoRecebeONumeroDoLote()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (a, b) = await DoisAparelhosAsync();
        await PullAsync(b);
        var lote = NovoId("b");

        (await PushAsync(a, Cadastro(Produto), Lote(lote))).Rejeitadas.Should().BeEmpty();

        var noB = (await PullAsync(b)).Should().ContainSingle(m => m.Tipo == "batch.upsert" && m.Id == lote).Which;
        noB.Payload.GetProperty("numeroCurto").GetString().Should().Be(await NumeroNoBancoAsync(lote));
    }

    [SkippableFact]
    public async Task NumeroMandadoPeloAparelho_EIgnorado()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (a, b) = await DoisAparelhosAsync();
        await PullAsync(b);
        var lote = NovoId("b");

        var resposta = await PushAsync(a, Cadastro(Produto), Lote(lote, numeroCurto: "99999999"));

        var noBanco = await NumeroNoBancoAsync(lote);
        noBanco.Should().NotBe("99999999");
        resposta.Numeros.Should().Contain(lote, noBanco);
        (await PullAsync(b)).Single(m => m.Id == lote).Payload.GetProperty("numeroCurto").GetString().Should().Be(noBanco);
    }

    [SkippableFact]
    public async Task LoteAntigoSemNumero_GanhaNumeroNaPromocaoENoPull()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (a, b) = await DoisAparelhosAsync();
        var lote = NovoId("b");
        (await PushAsync(a, Cadastro(Produto), Lote(lote))).Rejeitadas.Should().BeEmpty();
        await TirarNumeroAsync(lote);   // como um lote de antes da migração 20261104021847
        await PullAsync(b);

        // A descarta o lote: ele passa de novo pela promoção e ganha número.
        var resposta = await PushAsync(a, Cadastro(Produto), Lote(lote, descartado: true));

        var dado = await NumeroNoBancoAsync(lote);
        dado.Should().MatchRegex("^[0-9]{8}$");
        resposta.Numeros.Should().Contain(lote, dado);
        (await PullAsync(b)).Single(m => m.Id == lote).Payload.GetProperty("numeroCurto").GetString().Should().Be(dado);

        // Outro lote antigo que nenhum aparelho reenvia ganha número no pull completo de um aparelho novo.
        var antigo = NovoId("b");
        (await PushAsync(a, Cadastro(Produto), Lote(antigo))).Rejeitadas.Should().BeEmpty();
        await TirarNumeroAsync(antigo);
        var novo = await NovoAparelhoNaMesmaLojaAsync(a);

        var noNovo = (await PullAsync(novo)).Single(m => m.Id == antigo);

        var dadoNoPull = await NumeroNoBancoAsync(antigo);
        dadoNoPull.Should().MatchRegex("^[0-9]{8}$");
        noNovo.Payload.GetProperty("numeroCurto").GetString().Should().Be(dadoNoPull);
    }

    [SkippableFact]
    public async Task Isolamento_AparelhoDeOutraEmpresa_NaoRecebeONumeroDoLoteAlheio()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (a, _) = await DoisAparelhosAsync();
        var (outra, _) = await DoisAparelhosAsync();
        var lote = NovoId("b");
        (await PushAsync(a, Cadastro(Produto), Lote(lote))).Rejeitadas.Should().BeEmpty();
        var numeroDeA = await NumeroNoBancoAsync(lote);

        // A chave de um aparelho de outra empresa manda um lote com o mesmo id.
        var produtoDaOutra = NovoId("p");
        var resposta = await PushOuFalhaAsync(outra, Cadastro(produtoDaOutra), Lote(lote, produto: produtoDaOutra));

        if (resposta.Numeros.TryGetValue(lote, out var numero)) numero.Should().NotBe(numeroDeA);
        var noOutro = (await PullAsync(outra)).Where(m => m.Id == lote).Select(m => NumeroDoPayload(m.Payload)).ToList();
        noOutro.Should().NotContain(numeroDeA, "o pull do aparelho de outra empresa nunca leva o número do lote de A");
        (await NumeroNoBancoAsync(lote)).Should().Be(numeroDeA, "o lote da empresa A não muda");
    }

    [SkippableFact]
    public async Task Isolamento_AparelhoDeOutraLoja_NaoRecebeNemNumeraOLoteAlheioNoPush()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (a, _) = await DoisAparelhosAsync();
        var naA2 = await NovoAsync(a.EmpresaId, await NovaLojaAsync(a.EmpresaId));
        var lote = NovoId("b");
        (await PushAsync(a, Cadastro(Produto), Lote(lote))).Rejeitadas.Should().BeEmpty();
        await TirarNumeroAsync(lote);   // lote antigo da loja A1, ainda sem número

        // A chave da loja A2 reenvia uma mutation já aceita dela trocando o id do lote pelo da A1: o dedup aceita sem
        // aplicar, e o id do lote alheio chega à resposta.
        var produtoDaA2 = NovoId("p");
        var proprio = Lote(NovoId("b"), produto: produtoDaA2);
        (await PushAsync(naA2, Cadastro(produtoDaA2), proprio)).Rejeitadas.Should().BeEmpty();
        var resposta = await PushAsync(naA2, Lote(lote) with { Id = proprio.Id });

        resposta.Aceitas.Should().Contain(proprio.Id);
        resposta.Numeros.Should().NotContainKey(lote, "o número do lote da loja A1 não vai para o aparelho da A2");
        (await NumeroNoBancoAsync(lote)).Should().BeNull("o push da A2 não gasta o contador da A1");
    }

    // ---------- HTTP ----------

    private sealed record Aparelho(string DeviceId, HttpClient Http, Guid EmpresaId, Guid LojaId)
    {
        public long Cursor { get; set; }
    }

    private sealed record Envio(string Id, string Type, JsonElement Payload, long Ts);
    private sealed record Recebida(string Tipo, string Id, JsonElement Payload);
    private sealed record Resposta(List<string> Aceitas, List<string> Rejeitadas, Dictionary<string, string> Numeros);

    private async Task<(Aparelho A, Aparelho B)> DoisAparelhosAsync()
    {
        var (empresaId, lojaId) = await fixture.SeedEmpresaELojaAsync();
        return (await NovoAsync(empresaId, lojaId), await NovoAsync(empresaId, lojaId));
    }

    private Task<Aparelho> NovoAparelhoNaMesmaLojaAsync(Aparelho a) => NovoAsync(a.EmpresaId, a.LojaId);

    private async Task<Aparelho> NovoAsync(Guid empresaId, Guid lojaId)
    {
        var cred = await fixture.SeedMobileDeviceAsync(empresaId, lojaId);
        return new Aparelho(cred.DeviceId, fixture.CreateMobileClient(cred.ApiKey), empresaId, lojaId);
    }

    private static Task<Resposta> PushAsync(Aparelho aparelho, params Envio[] mutations) => EnviarAsync(aparelho, false, mutations);
    private static Task<Resposta> PushOuFalhaAsync(Aparelho aparelho, params Envio[] mutations) => EnviarAsync(aparelho, true, mutations);

    private static async Task<Resposta> EnviarAsync(Aparelho aparelho, bool aceitarFalha, Envio[] mutations)
    {
        var resp = await aparelho.Http.PostAsJsonAsync("/api/mobile/sync", new
        {
            deviceId = aparelho.DeviceId,
            operatorName = "Thati",
            mutations = mutations.Select(m => new { id = m.Id, deviceId = aparelho.DeviceId, type = m.Type, payload = m.Payload, ts = m.Ts }).ToArray()
        }, Json);
        if (aceitarFalha && resp.StatusCode != HttpStatusCode.OK) return new Resposta([], mutations.Select(m => m.Id).ToList(), []);
        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
        var corpo = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var aceitas = corpo.GetProperty("acceptedIds").EnumerateArray().Select(x => x.GetString()!).ToList();
        var rejeitadas = corpo.TryGetProperty("rejected", out var rej) && rej.ValueKind == JsonValueKind.Array
            ? rej.EnumerateArray().Select(r => r.GetProperty("mutationId").GetString()!).ToList()
            : [];
        var numeros = corpo.TryGetProperty("numerosCurtos", out var nums) && nums.ValueKind == JsonValueKind.Object
            ? nums.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!)
            : [];
        return new Resposta(aceitas, rejeitadas, numeros);
    }

    private static async Task<List<Recebida>> PullAsync(Aparelho aparelho)
    {
        var resp = await aparelho.Http.GetAsync($"/api/mobile/sync/pull?since={aparelho.Cursor}&deviceId={aparelho.DeviceId}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
        var corpo = await resp.Content.ReadFromJsonAsync<JsonElement>();
        aparelho.Cursor = corpo.GetProperty("serverTime").GetInt64();
        return corpo.GetProperty("mutations").EnumerateArray()
            .Select(m => new Recebida(
                m.GetProperty("type").GetString()!,
                m.GetProperty("payload").GetProperty("id").GetString()!,
                m.GetProperty("payload").Clone()))
            .ToList();
    }

    // Um produto por teste (o xUnit cria uma instância por teste), reenviado com o lote como o aparelho faz.
    private readonly string Produto = NovoId("p");

    private static Envio Cadastro(string id) => new("mut_" + Guid.NewGuid(), "product.upsert", JsonSerializer.SerializeToElement(
        new { id, name = "Lasanha", category = "massa", unit = "300g", price = 30m, stock = 3, custom = true }, Json), Ms(DateTime.UtcNow));

    private Envio Lote(string id, bool descartado = false, string? numeroCurto = null, string? produto = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["id"] = id, ["code"] = "LOT-" + id[^6..], ["lote"] = "LOT-261104",
            ["items"] = new[] { new { productId = produto ?? Produto, name = "Lasanha", emoji = (string?)null, unit = "300g", qty = 3, photo = (string?)null, weightG = 300 } },
            ["batchPhoto"] = null, ["createdAt"] = Ms(DateTime.UtcNow.AddMinutes(-1)),
        };
        if (descartado)
        {
            payload["discarded"] = true;
            payload["discardedAt"] = Ms(DateTime.UtcNow);
            payload["discardedBy"] = "Thati";
            payload["discardReason"] = "vencido";
        }
        if (numeroCurto is not null) payload["numeroCurto"] = numeroCurto;
        return new Envio("mut_" + Guid.NewGuid(), "batch.upsert", JsonSerializer.SerializeToElement(payload, Json), Ms(DateTime.UtcNow));
    }

    private static string? NumeroDoPayload(JsonElement payload) =>
        payload.TryGetProperty("numeroCurto", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;

    private static string NovoId(string prefixo) => $"{prefixo}-{Guid.NewGuid():N}"[..24];
    private static long Ms(DateTime utc) => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

    // ---------- banco ----------

    private async Task<string?> NumeroNoBancoAsync(string batchId)
    {
        using var scope = fixture.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        var loteId = await db.Set<Batch>().IgnoreQueryFilters().AsNoTracking()
            .Where(b => b.Id == batchId).Select(b => b.ErpLoteId).SingleAsync();
        loteId.Should().NotBeNull("o push promove o lote do aparelho a lote do ERP");
        return await db.Set<Lote>().IgnoreQueryFilters().AsNoTracking()
            .Where(l => l.Id == loteId).Select(l => l.NumeroCurto).SingleAsync();
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

    private async Task TirarNumeroAsync(string batchId)
    {
        using var scope = fixture.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        (await db.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE lotes SET "NumeroCurto" = NULL WHERE "MobileBatchId" = {batchId}""")).Should().Be(1);
    }
}

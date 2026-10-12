using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EasyStock.Application.Common;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Api.IntegrationTests.Mobile;

/// <summary>
/// #1520 (ADR-0060) de ponta a ponta: aparelhos da mesma empresa falando com a API por HTTP
/// (<c>POST /api/mobile/sync</c> e <c>GET /api/mobile/sync/pull</c>, header
/// <c>X-Mobile-Api-Key</c>), com PostgreSQL de verdade. O banco so e lido para conferir.
/// O vinculo com o ERP (inclusive o MovimentoCaixa do lancamento) roda dentro do proprio push,
/// pelo SyncAutoLinker, sem job.
/// </summary>
[Collection("MobileE2E")]
public class SyncModeloE2ETests(MobileE2EFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // ---------- a. estoque ----------

    [SkippableFact]
    public async Task Estoque_DoisAparelhos_CadaMovimentoContaUmaVez()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (a, b, _) = await TresAparelhosAsync();
        var produto = NovoId("p");
        var pedido = NovoId("o");

        // A cria o produto com saldo 10, como o PWA manda: cadastro + movimento no mesmo lote.
        (await PushAsync(a, Cadastro(produto, saldoNoAparelho: 10, porMovimento: true), Movimento(produto, +10)))
            .Rejeitadas.Should().BeEmpty();
        (await SaldoAsync(produto)).Should().Be(10);

        // A vende 1: pedido vai a "pronto", cadastro por movimento e movimento de -1.
        await PushAsync(a, Pedido(pedido, produto, "preparando", qtd: 1));
        (await PushAsync(a, Pedido(pedido, produto, "pronto", qtd: 1),
            Cadastro(produto, saldoNoAparelho: 9, porMovimento: true), Movimento(produto, -1)))
            .Rejeitadas.Should().BeEmpty();
        (await SaldoAsync(produto)).Should().Be(9, "aparelho 9, servidor 9 (antes ficava 8)");

        // B puxa e recebe o produto com 9.
        var noB = await PullAsync(b);
        noB.Should().ContainSingle(m => m.Tipo == "product.upsert" && m.Id == produto)
            .Which.Payload.GetProperty("stock").GetInt32().Should().Be(9);

        // B vende 2.
        var loteDeB = new[] { Cadastro(produto, saldoNoAparelho: 7, porMovimento: true), Movimento(produto, -2) };
        (await PushAsync(b, loteDeB)).Rejeitadas.Should().BeEmpty();
        (await SaldoAsync(produto)).Should().Be(7);

        // Reenvio do MESMO lote de B (mesmos MutationId): nada muda e a resposta e a mesma.
        var reenvio = await PushAsync(b, loteDeB);
        reenvio.Aceitas.Should().BeEquivalentTo(loteDeB.Select(m => m.Id));
        (await SaldoAsync(produto)).Should().Be(7);

        // A puxa e recebe 7.
        var noA = await PullAsync(a);
        noA.Should().ContainSingle(m => m.Tipo == "product.upsert" && m.Id == produto)
            .Which.Payload.GetProperty("stock").GetInt32().Should().Be(7);
    }

    // ---------- b. producao e descarte ----------

    [SkippableFact]
    public async Task ProducaoEDescarte_SomaEBaixaUmaVez_EAMarcaChegaAoOutroAparelho()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (a, b, _) = await TresAparelhosAsync();
        var produto = NovoId("p");
        var lote = NovoId("b");
        await PushAsync(a, Cadastro(produto, saldoNoAparelho: 7, porMovimento: true), Movimento(produto, +7));
        (await SaldoAsync(produto)).Should().Be(7);
        await PullAsync(b);   // B em dia antes da producao

        // A lanca um lote de 5.
        (await PushAsync(a, Cadastro(produto, saldoNoAparelho: 12, porMovimento: true),
            Lote(lote, produto, qtd: 5), Movimento(produto, +5))).Rejeitadas.Should().BeEmpty();
        (await SaldoAsync(produto)).Should().Be(12, "antes o lote era somado duas vezes");
        (await PullAsync(b)).Should().ContainSingle(m => m.Tipo == "batch.upsert" && m.Id == lote);

        // A descarta o lote.
        (await PushAsync(a, Cadastro(produto, saldoNoAparelho: 7, porMovimento: true),
            Lote(lote, produto, qtd: 5, descartado: true), Movimento(produto, -5))).Rejeitadas.Should().BeEmpty();
        (await SaldoAsync(produto)).Should().Be(7);

        // B puxa de novo: recebe a marca de descarte (lote antigo, alterado agora) e o saldo 7.
        var noB = await PullAsync(b);
        noB.Should().ContainSingle(m => m.Tipo == "batch.upsert" && m.Id == lote)
            .Which.Payload.GetProperty("discarded").GetBoolean().Should().BeTrue();
        noB.Should().ContainSingle(m => m.Tipo == "product.upsert" && m.Id == produto)
            .Which.Payload.GetProperty("stock").GetInt32().Should().Be(7);
    }

    // ---------- c. caixa ----------

    [SkippableFact]
    public async Task Caixa_EdicaoEExclusaoChegamAoServidorAoErpEAoOutroAparelho()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (a, b, _) = await TresAparelhosAsync();
        var lancamento = NovoId("cash");
        var agora = Ms(DateTime.UtcNow);

        // A lanca uma saida de 150 (digitou um zero a mais).
        (await PushAsync(a, Lancamento(lancamento, 150m, agora))).Rejeitadas.Should().BeEmpty();
        (await LancamentoAsync(lancamento)).Amount.Should().Be(150m);
        (await MovimentoDoLancamentoAsync(lancamento)).Valor.Should().Be(150m);
        (await PullAsync(b)).Should().ContainSingle(m => m.Id == lancamento)
            .Which.Payload.GetProperty("amount").GetDecimal().Should().Be(150m);

        // A corrige para 15: mesmo id.
        (await PushAsync(a, Lancamento(lancamento, 15m, agora))).Rejeitadas.Should().BeEmpty();
        (await LancamentoAsync(lancamento)).Amount.Should().Be(15m);
        var movimento = await MovimentoDoLancamentoAsync(lancamento);
        movimento.Valor.Should().Be(15m);
        movimento.EstornadoEm.Should().BeNull();
        (await PullAsync(b)).Should().ContainSingle(m => m.Tipo == "cashEntry.upsert" && m.Id == lancamento)
            .Which.Payload.GetProperty("amount").GetDecimal().Should().Be(15m);

        // A exclui.
        (await PushAsync(a, Mutation("cashEntry.delete", new { id = lancamento, type = "expense", amount = 15m, description = "Gas", createdAt = agora })))
            .Rejeitadas.Should().BeEmpty();
        (await LancamentoAsync(lancamento)).DeletedAt.Should().NotBeNull("a linha fica, marcada");
        movimento = await MovimentoDoLancamentoAsync(lancamento);
        movimento.EstornadoEm.Should().NotBeNull("no ERP a exclusao e estorno");
        movimento.Valor.Should().Be(15m);
        (await PullAsync(b)).Should().ContainSingle(m => m.Tipo == "cashEntry.upsert" && m.Id == lancamento)
            .Which.Payload.GetProperty("estornado").GetBoolean().Should().BeTrue();
    }

    [SkippableFact]
    public async Task Caixa_DiaFechado_RecusaEdicaoEExclusaoComMotivo_SemDerrubarOLote()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (a, _, _) = await TresAparelhosAsync();
        var lancamento = NovoId("cash");
        var outro = NovoId("cash");
        var ontem = DateTime.UtcNow.AddDays(-1);
        await PushAsync(a, Lancamento(lancamento, 40m, Ms(ontem)));

        // O caixa de ontem e fechado (pelo proprio sync: closing.upsert).
        (await PushAsync(a, Mutation("closing.upsert", new
        {
            id = NovoId("close"), dateKey = HorarioBrasil.DataOperacional(ontem).ToString("yyyy-MM-dd"),
            closedAt = Ms(DateTime.UtcNow), closedByName = "Thati",
            totalPagamentosPedidos = 0m, totalSaidasExtras = 40m, saldoFinal = -40m, notes = (string?)null
        }))).Rejeitadas.Should().BeEmpty();

        // Edicao e exclusao no dia fechado, no mesmo lote de um lancamento de hoje.
        var edicao = Lancamento(lancamento, 99m, Ms(ontem));
        var exclusao = Mutation("cashEntry.delete", new { id = lancamento, type = "expense", amount = 40m, description = "Gas", createdAt = Ms(ontem) });
        var deHoje = Lancamento(outro, 12m, Ms(DateTime.UtcNow));
        var resposta = await PushAsync(a, edicao, exclusao, deHoje);

        resposta.Aceitas.Should().BeEquivalentTo([deHoje.Id], "a recusa nao derruba o resto do lote");
        resposta.Rejeitadas.Should().HaveCount(2);
        resposta.Rejeitadas.Should().OnlyContain(r => r.Motivo.Contains("fechado"));
        (await LancamentoAsync(lancamento)).Amount.Should().Be(40m);
        (await LancamentoAsync(lancamento)).DeletedAt.Should().BeNull();
        var movimento = await MovimentoDoLancamentoAsync(lancamento);
        movimento.Valor.Should().Be(40m);
        movimento.EstornadoEm.Should().BeNull();
        (await LancamentoAsync(outro)).Amount.Should().Be(12m);
    }

    // ---------- d. carimbo do servidor ----------

    [SkippableFact]
    public async Task Carimbo_EdicaoOfflineAntigaChega_ERelogioAdiantadoNaoGeraConflitoFalso()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (a, b, _) = await TresAparelhosAsync();
        var produto = NovoId("p");
        await PushAsync(a, Cadastro(produto, saldoNoAparelho: 0, porMovimento: true));
        await PullAsync(a);   // cursor de A avanca ate agora

        // B sincroniza agora um pedido editado offline ha tres horas.
        var antigo = NovoId("o");
        var haTresHoras = DateTime.UtcNow.AddHours(-3);
        (await PushAsync(b, Pedido(antigo, produto, "preparando", qtd: 1, horaDoAparelho: haTresHoras) with { Ts = Ms(haTresHoras) }))
            .Rejeitadas.Should().BeEmpty();

        (await PullAsync(a)).Should().ContainSingle(m => m.Tipo == "order.upsert" && m.Id == antigo,
            "o cursor de A ja passou das tres horas atras; o que vale e o carimbo do servidor");

        // A, com o relogio dez minutos adiantado, grava um pedido. B (relogio certo) edita depois.
        var adiantado = NovoId("o");
        var daquiADezMinutos = DateTime.UtcNow.AddMinutes(10);
        await PushAsync(a, Pedido(adiantado, produto, "preparando", qtd: 1, horaDoAparelho: daquiADezMinutos) with { Ts = Ms(daquiADezMinutos) });
        await PullAsync(b);
        await Task.Delay(50);

        var deB = await PushAsync(b, Pedido(adiantado, produto, "pronto", qtd: 1));

        deB.Rejeitadas.Should().BeEmpty("a hora adiantada de A nao e a hora em que o servidor recebeu");
        (await PedidoAsync(adiantado)).Status.Should().Be("pronto");
    }

    // ---------- e. aparelho com PWA antigo ----------

    [SkippableFact]
    public async Task AparelhoAntigo_SaldoAbsolutoNoCadastro_ContinuaValendo()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (a, _, c) = await TresAparelhosAsync();
        var produto = NovoId("p");
        var pedido = NovoId("o");
        await PushAsync(a, Cadastro(produto, saldoNoAparelho: 9, porMovimento: true), Movimento(produto, +9));
        await PushAsync(a, Pedido(pedido, produto, "preparando", qtd: 1));

        // C nao manda stockByDelta nem stock.delta: o saldo do cadastro define, e a venda no mesmo
        // lote nao e descontada de novo.
        (await PushAsync(c, Cadastro(produto, saldoNoAparelho: 4, porMovimento: false), Pedido(pedido, produto, "pronto", qtd: 1)))
            .Rejeitadas.Should().BeEmpty();

        (await SaldoAsync(produto)).Should().Be(4);
    }

    // ---------- HTTP ----------

    private sealed record Aparelho(string DeviceId, HttpClient Http)
    {
        public long Cursor { get; set; }
    }

    private sealed record Envio(string Id, string Type, JsonElement Payload, long Ts);
    private sealed record Recebida(string Tipo, string Id, JsonElement Payload, long Ts);
    private sealed record Recusa(string Id, string Motivo);
    private sealed record Resposta(List<string> Aceitas, List<Recusa> Rejeitadas);

    private async Task<(Aparelho A, Aparelho B, Aparelho C)> TresAparelhosAsync()
    {
        var (empresaId, lojaId) = await fixture.SeedEmpresaELojaAsync();
        async Task<Aparelho> NovoAsync()
        {
            var cred = await fixture.SeedMobileDeviceAsync(empresaId, lojaId);
            return new Aparelho(cred.DeviceId, fixture.CreateMobileClient(cred.ApiKey));
        }
        return (await NovoAsync(), await NovoAsync(), await NovoAsync());
    }

    private static async Task<Resposta> PushAsync(Aparelho aparelho, params Envio[] mutations)
    {
        var resp = await aparelho.Http.PostAsJsonAsync("/api/mobile/sync", new
        {
            deviceId = aparelho.DeviceId,
            operatorName = "Thati",
            mutations = mutations.Select(m => new { id = m.Id, deviceId = aparelho.DeviceId, type = m.Type, payload = m.Payload, ts = m.Ts })
        }, Json);
        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
        var corpo = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var aceitas = corpo.GetProperty("acceptedIds").EnumerateArray().Select(x => x.GetString()!).ToList();
        var rejeitadas = corpo.TryGetProperty("rejected", out var rej) && rej.ValueKind == JsonValueKind.Array
            ? rej.EnumerateArray().Select(r => new Recusa(r.GetProperty("mutationId").GetString()!, r.GetProperty("reason").GetString()!)).ToList()
            : [];
        return new Resposta(aceitas, rejeitadas);
    }

    /// <summary>Pull a partir do cursor do aparelho; o cursor avanca para o serverTime da resposta, como no PWA.</summary>
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
                m.GetProperty("payload").Clone(),
                m.GetProperty("ts").GetInt64()))
            .ToList();
    }

    // ---------- mutations como o PWA manda ----------

    private static Envio Mutation(string tipo, object payload) => new(
        "mut_" + Guid.NewGuid(), tipo, JsonSerializer.SerializeToElement(payload, Json), Ms(DateTime.UtcNow));

    private static Envio Cadastro(string id, int saldoNoAparelho, bool porMovimento) => Mutation("product.upsert", porMovimento
        ? new { id, name = "Lasanha", category = "massa", unit = "300g", price = 30m, stock = saldoNoAparelho, custom = true, stockByDelta = true }
        : new { id, name = "Lasanha", category = "massa", unit = "300g", price = 30m, stock = saldoNoAparelho, custom = true });

    private static Envio Movimento(string produto, int qtd) => Mutation("stock.delta",
        new { id = "sd_" + Guid.NewGuid(), productId = produto, qty = qtd });

    private static Envio Pedido(string id, string produto, string status, int qtd, DateTime? horaDoAparelho = null)
    {
        var hora = Ms(horaDoAparelho ?? DateTime.UtcNow);
        return Mutation("order.upsert", new
        {
            id, clientId = (string?)null, clientSnapshot = new { name = "Ana", @ref = (string?)null },
            items = new[] { new { productId = produto, name = "Lasanha", emoji = (string?)null, unit = "300g", qty = qtd, unitPrice = 30m } },
            notes = (string?)null, total = 30m * qtd, status, createdAt = hora, updatedAt = hora
        });
    }

    private static Envio Lote(string id, string produto, int qtd, bool descartado = false) => Mutation("batch.upsert", new
    {
        id, code = "LOT-" + id[^6..], lote = "LOT-261010",
        items = new[] { new { productId = produto, name = "Lasanha", emoji = (string?)null, unit = "300g", qty = qtd, photo = (string?)null, weightG = 300 } },
        batchPhoto = (string?)null, createdAt = Ms(DateTime.UtcNow.AddMinutes(-1)),
        discarded = descartado ? true : (bool?)null,
        discardedAt = descartado ? Ms(DateTime.UtcNow) : (long?)null,
        discardedBy = descartado ? "Thati" : null,
        discardReason = descartado ? "vencido" : null
    });

    private static Envio Lancamento(string id, decimal valor, long criadoEm) => Mutation("cashEntry.upsert",
        new { id, type = "expense", amount = valor, description = "Gas", metodo = "pix", createdAt = criadoEm });

    private static string NovoId(string prefixo) => $"{prefixo}-{Guid.NewGuid():N}"[..24];
    private static long Ms(DateTime utc) => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

    // ---------- conferencia no banco (so leitura) ----------

    private async Task<T> LerAsync<T>(Func<EasyStockDbContext, Task<T>> consulta)
    {
        using var scope = fixture.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        return await consulta(db);
    }

    private Task<int> SaldoAsync(string produto) => LerAsync(db =>
        db.Set<Product>().AsNoTracking().Where(p => p.Id == produto).Select(p => p.Stock).SingleAsync());

    private Task<CashEntry> LancamentoAsync(string id) => LerAsync(db =>
        db.Set<CashEntry>().AsNoTracking().SingleAsync(c => c.Id == id));

    private Task<Order> PedidoAsync(string id) => LerAsync(db =>
        db.Set<Order>().AsNoTracking().SingleAsync(o => o.Id == id));

    private Task<MovimentoCaixa> MovimentoDoLancamentoAsync(string lancamento) => LerAsync(db =>
        db.Set<MovimentoCaixa>().IgnoreQueryFilters().AsNoTracking().SingleAsync(m => m.Referencia == "mobile:" + lancamento));
}

using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
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
using Microsoft.IdentityModel.Tokens;

namespace EasyStock.Api.IntegrationTests.Mobile;

/// <summary>
/// #2813 (B4): os fechamentos de caixa do aparelho ficam CONSULTAVEIS no ERP, somente leitura. O import ja guarda o
/// snapshot inteiro em DeviceBackup e trata cashClosings como archive-only; o endpoint so le dali. Nao abre nem fecha
/// caixa, nao cria lancamento e nao mexe em saldo. Repo publico: todo dado abaixo e ficticio.
/// </summary>
[Collection("MobileE2E")]
public class LegacyFechamentosLeituraE2ETests(MobileE2EFixture fixture)
{
    private const string Rota = "/api/mobile/legacy-import/fechamentos";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly DateTimeOffset Inicio = new(2026, 1, 1, 21, 30, 0, TimeSpan.Zero);
    private sealed record Ctx(MobileDeviceCredentials Device, HttpClient Http);
    private sealed record ContagemCaixa(int Fechamentos, int LancamentosDoAparelho, int Movimentos);

    [SkippableFact]
    public async Task Importa37Fechamentos_EndpointDevolve37EmOrdem_SemTocarNoCaixa()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var c = await NovoContextoAsync();
        var empresa = c.Device.EmpresaId;
        var antes = await ContarCaixaAsync(empresa);
        var request = Pedir(c.Device, Fechamentos37());

        var recibo = await ImportarAsync(c.Http, request);

        recibo.Counts["cashClosings"].Should().Be(37);
        recibo.ArchiveOnlyCollections.Should().Equal("cashClosings");
        using var gerente = await ClienteWebAsync(empresa, "Gerente");
        var lista = await ListarAsync(gerente);
        var itens = lista.GetProperty("itens").EnumerateArray().ToList();
        itens.Should().HaveCount(37);
        lista.GetProperty("ignorados").GetInt32().Should().Be(0);
        itens.Select(x => x.GetProperty("id").GetString()).Should().OnlyHaveUniqueItems();
        // dateKey decrescente; no mesmo dia, o fechamento mais tardio primeiro.
        itens.Select(x => x.GetProperty("id").GetString()).Take(3).Should().Equal("fch-35-tarde", "fch-35", "fch-34");
        var ordem = itens.Select(x => (DateKey: x.GetProperty("dateKey").GetString()!, ClosedAt: x.GetProperty("closedAt").GetDateTimeOffset())).ToList();
        ordem.Should().Equal(ordem.OrderByDescending(x => x.DateKey, StringComparer.Ordinal).ThenByDescending(x => x.ClosedAt));

        var maisNovo = itens[0];
        maisNovo.GetProperty("dateKey").GetString().Should().Be(Dia(35));
        maisNovo.GetProperty("closedAt").GetString().Should().EndWith("Z", "ISO 8601 em UTC");
        maisNovo.GetProperty("closedAt").GetDateTimeOffset().Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(Ms(35, 135)));
        maisNovo.GetProperty("closedBy").GetString().Should().Be("Operadora Teste");
        maisNovo.GetProperty("notes").GetString().Should().Be("Nota de teste");
        maisNovo.GetProperty("origem").GetString().Should().Be("aparelho-legado");
        maisNovo.GetProperty("importId").GetGuid().Should().Be(request.ImportId);
        var totais = maisNovo.GetProperty("totals");
        totais.GetProperty("sold").GetDecimal().Should().Be(40.25m, "valores em reais, como no snapshot; nao converte para centavos");
        totais.GetProperty("in").GetDecimal().Should().Be(10m);
        totais.GetProperty("out").GetDecimal().Should().Be(5m);
        totais.GetProperty("balance").GetDecimal().Should().Be(45.25m);
        totais.GetProperty("conferredBalance").GetDecimal().Should().Be(39.75m);
        totais.GetProperty("entriesCount").GetInt32().Should().Be(2);
        totais.GetProperty("ordersCount").GetInt32().Should().Be(5);
        itens[1].GetProperty("notes").ValueKind.Should().Be(JsonValueKind.Null, "notes nulo continua nulo");

        var depois = await ContarCaixaAsync(empresa);
        antes.Fechamentos.Should().Be(0);
        depois.Should().Be(antes, "ler fechamento do aparelho nao grava FechamentoCaixa, lancamento do aparelho nem movimento de caixa");
    }

    [SkippableFact]
    public async Task ReimportarOMesmoImportId_Replayed_ContinuaCom37SemDuplicar()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var c = await NovoContextoAsync();
        var empresa = c.Device.EmpresaId;
        var request = Pedir(c.Device, Fechamentos37());

        var primeiro = await ImportarAsync(c.Http, request);
        var repetido = await ImportarAsync(c.Http, request);

        primeiro.Replayed.Should().BeFalse();
        repetido.Replayed.Should().BeTrue();
        using var gerente = await ClienteWebAsync(empresa, "Gerente");
        var lista = await ListarAsync(gerente);
        lista.GetProperty("itens").GetArrayLength().Should().Be(37);
        await LerAsync(empresa, async db =>
        {
            (await db.Set<DeviceBackup>().IgnoreQueryFilters().CountAsync(x => x.EmpresaId == empresa)).Should().Be(1, "o replay nao grava um segundo backup");
            return 0;
        });
    }

    [SkippableFact]
    public async Task OutraEmpresa_NaoVeOsFechamentos_ListaVazia()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var c = await NovoContextoAsync();
        await ImportarAsync(c.Http, Pedir(c.Device, Fechamentos37()));
        var (outraEmpresa, _) = await fixture.SeedEmpresaELojaAsync();
        using var dona = await ClienteWebAsync(c.Device.EmpresaId, "Gerente");
        using var outra = await ClienteWebAsync(outraEmpresa, "Gerente");

        var minha = await ListarAsync(dona);
        var alheia = await ListarAsync(outra);

        minha.GetProperty("itens").GetArrayLength().Should().Be(37);
        alheia.GetProperty("itens").GetArrayLength().Should().Be(0, "o escopo e a empresa do token");
        alheia.GetProperty("ignorados").GetInt32().Should().Be(0);
    }

    [SkippableFact]
    public async Task SemToken_Retorna401_EAChaveDoAparelhoSozinhaNaoLe()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var c = await NovoContextoAsync();
        await ImportarAsync(c.Http, Pedir(c.Device, Fechamentos37()));
        using var anonimo = fixture.CreateClient();

        await AssertStatusAsync(anonimo, HttpStatusCode.Unauthorized);
        await AssertStatusAsync(c.Http, HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task SemPolicyGerente_Retorna403_EGerenteLe()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var c = await NovoContextoAsync();
        await ImportarAsync(c.Http, Pedir(c.Device, Fechamentos37()));
        var empresa = c.Device.EmpresaId;
        using var operador = await ClienteWebAsync(empresa, "Operador");
        using var visualizador = await ClienteWebAsync(empresa, "Visualizador");
        using var gerente = await ClienteWebAsync(empresa, "Gerente");

        await AssertStatusAsync(operador, HttpStatusCode.Forbidden);
        await AssertStatusAsync(visualizador, HttpStatusCode.Forbidden);
        await AssertStatusAsync(gerente, HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task ConferredBalanceNulo_FicaNulo_EFechamentoInvalidoEIgnoradoEContado()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var c = await NovoContextoAsync();
        var comNulo = Fechamento("fch-nulo", "2026-03-10", Ms(68), conferido: null);
        var comValor = Fechamento("fch-conferido", "2026-03-09", Ms(67), conferido: 95.5m);
        var semDia = Fechamento("fch-sem-dia", "2026-03-08", Ms(66)); semDia.Remove("dateKey");
        var tipoErrado = Fechamento("fch-tipo-errado", "2026-03-07", Ms(65)); tipoErrado["closedAt"] = "ontem a noite";
        var semResumo = Fechamento("fch-sem-resumo", "2026-03-06", Ms(64)); semResumo.Remove("summary");
        await ImportarAsync(c.Http, Pedir(c.Device, [comNulo, comValor, semDia, tipoErrado, semResumo]));
        using var gerente = await ClienteWebAsync(c.Device.EmpresaId, "Gerente");

        var lista = await ListarAsync(gerente);

        var itens = lista.GetProperty("itens").EnumerateArray().ToList();
        itens.Select(x => x.GetProperty("id").GetString()).Should().Equal("fch-nulo", "fch-conferido");
        lista.GetProperty("ignorados").GetInt32().Should().Be(3, "sem dateKey, closedAt de tipo errado e sem summary: pula o item, nao derruba a lista");
        itens[0].GetProperty("totals").GetProperty("conferredBalance").ValueKind.Should().Be(JsonValueKind.Null);
        itens[1].GetProperty("totals").GetProperty("conferredBalance").GetDecimal().Should().Be(95.5m);
    }

    [SkippableFact]
    public async Task DoisImportsComOMesmoIdDeFechamento_DevolveUmItem_DoImportMaisRecente()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (empresa, loja1) = await fixture.SeedEmpresaELojaAsync();
        var loja2 = await SeedLojaAsync(empresa);
        var aparelho1 = await fixture.SeedMobileDeviceAsync(empresa, loja1);
        var aparelho2 = await fixture.SeedMobileDeviceAsync(empresa, loja2);
        var primeiro = Pedir(aparelho1, [Fechamento("fch-repetido", "2026-04-01", Ms(90), sold: 10m, notas: "do primeiro import")]);
        var segundo = Pedir(aparelho2, [Fechamento("fch-repetido", "2026-04-01", Ms(90), sold: 20m, notas: "do segundo import")]);
        await ImportarAsync(fixture.CreateMobileClient(aparelho1.ApiKey), primeiro);
        await ImportarAsync(fixture.CreateMobileClient(aparelho2.ApiKey), segundo);
        using var gerente = await ClienteWebAsync(empresa, "Gerente");

        var lista = await ListarAsync(gerente);

        var itens = lista.GetProperty("itens").EnumerateArray().ToList();
        itens.Should().HaveCount(1, "o mesmo id de fechamento nao aparece duas vezes");
        itens[0].GetProperty("importId").GetGuid().Should().Be(segundo.ImportId, "vence o import mais recente");
        itens[0].GetProperty("notes").GetString().Should().Be("do segundo import");
        itens[0].GetProperty("totals").GetProperty("sold").GetDecimal().Should().Be(20m);
        lista.GetProperty("ignorados").GetInt32().Should().Be(0, "duplicata de import mais antigo nao e item invalido");
    }

    // ---------- apoio ----------

    private static string Sha(string json) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    private static string Dia(int i) => DateOnly.FromDateTime(Inicio.UtcDateTime.AddDays(i)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static long Ms(int dia, int minutosApos = 0) => Inicio.AddDays(dia).AddMinutes(minutosApos).ToUnixTimeMilliseconds();

    /// <summary>Forma real do item no snapshot do aparelho; valores em reais.</summary>
    private static Dictionary<string, object?> Fechamento(string id, string dateKey, long closedAt, decimal sold = 100.5m, decimal? conferido = null, string? notas = null, int pedidos = 3) => new()
    {
        ["id"] = id, ["dateKey"] = dateKey, ["closedAt"] = closedAt, ["closedBy"] = "Operadora Teste", ["notes"] = notas,
        ["summary"] = new Dictionary<string, object?>
        {
            ["balance"] = sold + 5m, ["conferredBalance"] = conferido, ["entriesCount"] = 2, ["in"] = 10m,
            ["ordersCount"] = pedidos, ["out"] = 5m, ["sold"] = sold
        }
    };

    /// <summary>36 dias, um fechamento por dia, mais um segundo fechamento no ultimo dia: 37.</summary>
    private static List<object> Fechamentos37()
    {
        var lista = new List<object>();
        for (var i = 0; i < 36; i++) lista.Add(Fechamento($"fch-{i:D2}", Dia(i), Ms(i), sold: 100m + i + 0.5m, pedidos: i % 7 + 1));
        lista.Add(Fechamento("fch-35-tarde", Dia(35), Ms(35, 135), sold: 40.25m, conferido: 39.75m, notas: "Nota de teste", pedidos: 5));
        return lista;
    }

    private static LegacySnapshotRequest Pedir(MobileDeviceCredentials d, IEnumerable<object> fechamentos)
    {
        var pid = "p-" + Guid.NewGuid().ToString("N");
        var json = JsonSerializer.Serialize(new
        {
            products = new[] { new { id = pid, name = "Produto " + pid, category = "massa", stock = 7, price = 10m, cost = 3m, unit = "un" } },
            clients = Array.Empty<object>(), orders = Array.Empty<object>(), batches = Array.Empty<object>(),
            cashEntries = Array.Empty<object>(), cashClosings = fechamentos.ToArray()
        }, Json);
        return new LegacySnapshotRequest(Guid.NewGuid(), json, Sha(json), ["mut_" + Guid.NewGuid()], true, d.EmpresaId, d.LojaId, "Operacao");
    }

    private async Task<Ctx> NovoContextoAsync()
    {
        var (empresa, loja) = await fixture.SeedEmpresaELojaAsync();
        var device = await fixture.SeedMobileDeviceAsync(empresa, loja);
        return new Ctx(device, fixture.CreateMobileClient(device.ApiKey));
    }

    private async Task<Guid> SeedLojaAsync(Guid empresa)
    {
        using var scope = fixture.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        using var semRls = db.UseRowLevelSecurityBypass(); // teste semeia sem tenant; a API roda sob RLS (#2180)
        var loja = new Loja { Id = Guid.NewGuid(), EmpresaId = empresa, Nome = "Segunda Loja Teste", Ativa = true, CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow };
        db.Lojas.Add(loja);
        await db.SaveChangesAsync();
        return loja.Id;
    }

    private static async Task<LegacySnapshotResponse> ImportarAsync(HttpClient http, LegacySnapshotRequest request)
    {
        var response = await http.PostAsJsonAsync("/api/mobile/legacy-import", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<LegacySnapshotResponse>())!;
    }

    private static async Task<JsonElement> ListarAsync(HttpClient http)
    {
        var resposta = await http.GetAsync(Rota);
        var corpo = await resposta.Content.ReadAsStringAsync();
        resposta.StatusCode.Should().Be(HttpStatusCode.OK, corpo);
        using var documento = JsonDocument.Parse(corpo);
        return documento.RootElement.Clone();
    }

    private static async Task AssertStatusAsync(HttpClient http, HttpStatusCode esperado)
    {
        var resposta = await http.GetAsync(Rota);
        resposta.StatusCode.Should().Be(esperado, await resposta.Content.ReadAsStringAsync());
    }

    private Task<ContagemCaixa> ContarCaixaAsync(Guid empresa) => LerAsync(empresa, async db => new ContagemCaixa(
        await db.Set<FechamentoCaixa>().IgnoreQueryFilters().CountAsync(x => x.EmpresaId == empresa),
        await db.Set<CashEntry>().IgnoreQueryFilters().CountAsync(x => x.EmpresaId == empresa),
        await db.Set<MovimentoCaixa>().IgnoreQueryFilters().CountAsync(x => x.EmpresaId == empresa)));

    private async Task<T> LerAsync<T>(Guid empresa, Func<EasyStockDbContext, Task<T>> acao)
    {
        using var scope = fixture.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        db.SetMobileTenantContext(empresa);
        return await acao(db);
    }

    /// <summary>JWT como o login emite, com o nivel pedido. A leitura de gestao exige a policy Gerente.</summary>
    private async Task<HttpClient> ClienteWebAsync(Guid empresa, string nivel)
    {
        Guid usuarioId;
        using (var scope = fixture.Factory!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            using var bypass = db.UseRowLevelSecurityBypass();
            var usuario = Usuario.Criar("Teste " + nivel, $"fechamentos-{Guid.NewGuid():N}@example.test", "sem-login-por-senha");
            db.Usuarios.Add(usuario);
            await db.SaveChangesAsync();
            usuarioId = usuario.Id;
        }
        var claims = new List<Claim>
        {
            new("sub", usuarioId.ToString()),
            new("nivel", nivel),
            new("empresaId", empresa.ToString()),
            new("iat", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };
        var chave = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("EasyStock-Test-SuperSecretKey-Min32Chars!!"));
        var jwt = new JwtSecurityToken("EasyStock", "EasyStock", claims, expires: DateTime.UtcNow.AddMinutes(20),
            signingCredentials: new SigningCredentials(chave, SecurityAlgorithms.HmacSha256));
        var cliente = fixture.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(jwt));
        return cliente;
    }
}

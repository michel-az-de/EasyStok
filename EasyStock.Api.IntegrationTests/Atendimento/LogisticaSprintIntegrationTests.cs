using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Api.IntegrationTests.Atendimento;

public sealed partial class PedidoConversaConcorrenciaTests
{
    [SkippableFact]
    public async Task Logistica_DezPedidos_ContratacaoPagamentoERetorno_SemDuplicarCaixa()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        var fixture = Environment.GetEnvironmentVariable("EASYSTOK_LOGISTICA_BROWSER_FIXTURE");
        await using var factory = CriarFactory(nivel: NivelAcesso.Admin).WithWebHostBuilder(b =>
        {
            b.UseSetting("Logistica:EmpresaDemonstracaoId", EmpresaId.ToString());
            if (!string.IsNullOrWhiteSpace(fixture)) b.UseSetting("urls", "http://127.0.0.1:0");
        });
        if (!string.IsNullOrWhiteSpace(fixture)) factory.UseKestrel(0);
        using var client = factory.CreateClient();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(EmpresaId);
            db.Empresas.Add(new Empresa { Id = EmpresaId, Nome = "Demonstração de entregas",
                Documento = EmpresaId.ToString("N")[..14], CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
            db.Usuarios.Add(new Usuario { Id = UsuarioId, Nome = "Gerente da demonstração", Email = "logistica@fixture.test", SenhaHash = "sem-login-externo", Ativo = true, CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
            await db.SaveChangesAsync();
            (await db.Lojas.AnyAsync(l => l.EmpresaId == EmpresaId)).Should().BeFalse();
            (await db.Storefronts.AnyAsync(s => s.EmpresaId == EmpresaId)).Should().BeFalse();
        }
        var dia = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1).ToString("yyyy-MM-dd");
        var seed = await LogisticaOk(client.PostAsJsonAsync("/api/atendimento/logistica/demonstracao", new { dia }));
        seed.GetProperty("criados").GetInt32().Should().Be(10);
        var repetirSeed = await LogisticaOk(client.PostAsJsonAsync("/api/atendimento/logistica/demonstracao", new { dia }));
        repetirSeed.GetProperty("criados").GetInt32().Should().Be(0);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(EmpresaId);
            var vitrine = await db.Storefronts.SingleAsync(s => s.EmpresaId == EmpresaId);
            vitrine.Ativo.Should().BeFalse();
            vitrine.Slug.Should().Be($"demo-{EmpresaId:N}");
            vitrine.Slug.Length.Should().BeLessThanOrEqualTo(40);
            (await db.Lojas.CountAsync(l => l.EmpresaId == EmpresaId)).Should().Be(1);
            (await db.Pedidos.CountAsync(p => p.EmpresaId == EmpresaId)).Should().Be(10);
        }
        var contexto = await LogisticaOk(client.GetAsync($"/api/atendimento/logistica/contexto?dia={dia}"));
        contexto.GetProperty("demonstracao").GetBoolean().Should().BeTrue();
        contexto.GetProperty("pedidos").GetArrayLength().Should().Be(10);
        var pedidos = contexto.GetProperty("pedidos").EnumerateArray().OrderBy(p => p.GetProperty("cliente").GetString()).ToArray();
        var ids = pedidos.Take(3).Select(p => p.GetProperty("id").GetGuid()).ToArray();
        var cotacoes = await LogisticaOk(client.PostAsJsonAsync("/api/atendimento/logistica/cotacoes", new { pedidoIds = ids, agrupar = true }));
        var cotacao = cotacoes.EnumerateArray().Single(q => q.GetProperty("provedor").GetString() == "lalamove" && q.GetProperty("indisponibilidade").ValueKind == JsonValueKind.Null);
        var contratar = new { cotacaoId = cotacao.GetProperty("id").GetGuid(), chaveIdempotencia = "contrato-sprint-repetido" };
        var contratos = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => LogisticaOk(client.PostAsJsonAsync("/api/atendimento/logistica/despachos", contratar))));
        var despachoId = contratos[0].GetProperty("id").GetGuid();
        contratos[1].GetProperty("id").GetGuid().Should().Be(despachoId);
        var link = contratos[0].GetProperty("rastreioUrl").GetString()!;
        var acompanhamento = await client.GetStringAsync(link);
        acompanhamento.Should().Contain("acompanhamento de demonstração").And.NotContain("Teste 01").And.NotContain("+5500000000000");
        (await client.GetAsync(link + "0")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var pagamento = new { chaveIdempotencia = "pagamento-sprint-repetido", meio = "pix", referencia = "pagamento-ficticio-confirmado" };
        var pagamentos = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => LogisticaOk(client.PostAsJsonAsync($"/api/atendimento/logistica/despachos/{despachoId}/pagamento", pagamento))));
        pagamentos.All(p => p.GetProperty("pago").GetBoolean()).Should().BeTrue();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(EmpresaId);
            var saidas = await db.MovimentosCaixa.Where(m => m.EmpresaId == EmpresaId && m.Tipo == "saida").ToListAsync();
            saidas.Should().ContainSingle();
            saidas[0].Valor.Should().Be(cotacao.GetProperty("valorCentavos").GetInt32() / 100m);
        }
        var semEndereco = pedidos.Single(p => p.GetProperty("cliente").GetString()!.Contains("Elisa")).GetProperty("id").GetGuid();
        (await client.PostAsJsonAsync("/api/atendimento/logistica/cotacoes", new { pedidoIds = new[] { semEndereco }, agrupar = false })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync("/api/atendimento/logistica/cotacoes", new { pedidoIds = new[] { Guid.NewGuid() }, agrupar = false })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync("/api/atendimento/logistica/automatico", new { pedidoIds = new[] { pedidos[3].GetProperty("id").GetGuid() }, agrupar = false, chaveIdempotencia = "auto-desligado" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        if (!string.IsNullOrWhiteSpace(fixture))
        {
            await File.WriteAllTextAsync(fixture, JsonSerializer.Serialize(new { api = client.BaseAddress!.ToString().TrimEnd('/'), empresaId = EmpresaId, usuarioId = UsuarioId, dia }));
            using var limite = new CancellationTokenSource(TimeSpan.FromMinutes(30));
            while (!File.Exists(fixture + ".stop")) await Task.Delay(500, limite.Token);
        }
    }

    private static async Task<JsonElement> LogisticaOk(Task<HttpResponseMessage> request)
    {
        using var response = await request;
        var body = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue($"HTTP {(int)response.StatusCode}: {body}");
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("data").Clone();
    }
}

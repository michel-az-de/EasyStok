using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Api.IntegrationTests.Atendimento;

public sealed partial class PedidoConversaConcorrenciaTests
{
    /// <summary>
    /// #2342 (R2-API-4): o caixa aceita <c>lojaId</c> do corpo e da query. Pela API real (DI, RLS e banco), a loja de
    /// OUTRA empresa não abre caixa, não recebe lançamento e não mostra o caixa dela. O furo que sobra na issue é entre
    /// lojas da mesma empresa, e esse depende de decisão de produto.
    /// </summary>
    [SkippableFact]
    public async Task Caixa_LojaDeOutraEmpresaNaoAbreNaoLancaENaoMostraOCaixaDela()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();
        using var client = factory.CreateClient();
        await SemearAsync(factory);
        var outraEmpresa = Guid.NewGuid();
        var lojaAlheia = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(outraEmpresa);
            var agora = DateTime.UtcNow;
            db.Empresas.Add(new Empresa { Id = outraEmpresa, Nome = "Outra empresa", Documento = outraEmpresa.ToString("N")[..14], CriadoEm = agora, AlteradoEm = agora });
            db.Lojas.Add(new Loja { Id = lojaAlheia, EmpresaId = outraEmpresa, Nome = "Loja alheia", Ativa = true, CriadoEm = agora, AlteradoEm = agora });
            db.MovimentosCaixa.Add(MovimentoCaixa.Criar(outraEmpresa, "abertura", 777m, lojaId: lojaAlheia));
            await db.SaveChangesAsync();
        }

        var abrir = await client.PostAsJsonAsync("/api/caixa/abrir", new { EmpresaId, LojaId = lojaAlheia, SaldoInicial = 10m });
        abrir.StatusCode.Should().Be(HttpStatusCode.BadRequest, await abrir.Content.ReadAsStringAsync());

        var lancar = await client.PostAsJsonAsync("/api/caixa/movimentos", new { EmpresaId, LojaId = lojaAlheia, Tipo = "entrada", Valor = 5m });
        lancar.StatusCode.Should().Be(HttpStatusCode.BadRequest, await lancar.Content.ReadAsStringAsync());

        using var dia = JsonDocument.Parse(await client.GetStringAsync($"/api/caixa/dia?lojaId={lojaAlheia}"));
        var caixa = dia.RootElement.GetProperty("data");
        caixa.GetProperty("saldoInicial").GetDecimal().Should().Be(0m, "a abertura de 777 é da outra empresa");
        caixa.GetProperty("aberto").GetBoolean().Should().BeFalse();

        using var leitura = factory.Services.CreateScope();
        var dbLeitura = leitura.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        dbLeitura.SetMobileTenantContext(EmpresaId);
        (await dbLeitura.MovimentosCaixa.IgnoreQueryFilters().CountAsync(m => m.LojaId == lojaAlheia && m.EmpresaId == EmpresaId))
            .Should().Be(0, "nenhum movimento nasce com a loja de outra empresa");
    }
}

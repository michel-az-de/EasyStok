using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Api.IntegrationTests.Mobile;

[Collection("MobileE2E")]
public class AutoTicketE2ETests(MobileE2EFixture fixture)
{
    [Fact]
    public async Task POST_sem_X_Ci_Key_retorna_401()
    {
        if (!fixture.IsAvailable) return;
        await fixture.SeedOwnerEmpresaAsync();

        using var client = fixture.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/ci/tickets", new
        {
            origin = "ci",
            signature = "abc",
            titulo = "x",
            descricao = "y",
            contexto = (string?)null
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task POST_cria_ticket_BugFixDev_com_X_Ci_Key()
    {
        if (!fixture.IsAvailable) return;
        await fixture.SeedOwnerEmpresaAsync();

        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Add("X-Ci-Key", MobileE2EFixture.TestCiKey);

        var resp = await client.PostAsJsonAsync("/api/ci/tickets", new
        {
            origin = "runtime",
            signature = "test-sig-001",
            titulo = "Endpoint /api/mobile/version degradado",
            descricao = "5 falhas consecutivas",
            contexto = "endpoint=/api/mobile/version"
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("created").GetBoolean().Should().BeTrue();
        body.GetProperty("prioridade").GetString().Should().Be("Critica");

        // Verifica no DB que o ticket realmente entrou com Categoria BugFixDev
        using var scope = fixture.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        var ticket = await db.AdminTickets
            .Where(t => t.EmpresaId == MobileE2EFixture.OwnerEmpresaId
                     && t.Categoria == TicketCategoria.BugFixDev)
            .OrderByDescending(t => t.CriadoEm)
            .FirstAsync();
        ticket.Prioridade.Should().Be(TicketPrioridade.Critica);
        ticket.Titulo.Should().StartWith("[CI test-sig-001]");
    }

    [Fact]
    public async Task POST_com_mesma_signature_no_mesmo_dia_anexa_comentario_sem_duplicar()
    {
        if (!fixture.IsAvailable) return;
        await fixture.SeedOwnerEmpresaAsync();

        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Add("X-Ci-Key", MobileE2EFixture.TestCiKey);

        var payload = new
        {
            origin = "ci",
            signature = "dedup-sig-xyz",
            titulo = "teste ci falhando",
            descricao = "primeira ocorrencia",
            contexto = (string?)null
        };

        var first = await client.PostAsJsonAsync("/api/ci/tickets", payload);
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var second = await client.PostAsJsonAsync("/api/ci/tickets", payload);
        second.StatusCode.Should().Be(HttpStatusCode.OK); // sem novo ticket
        var body = await second.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("created").GetBoolean().Should().BeFalse();

        // DB: 1 ticket pra essa signature
        using var scope = fixture.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        var count = await db.AdminTickets
            .CountAsync(t => t.Titulo.StartsWith("[CI dedup-sig-xyz]"));
        count.Should().Be(1);
    }
}

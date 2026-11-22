using System.Net;
using EasyStock.Application.Services.Pedidos;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Api.IntegrationTests.Mobile;

/// <summary>
/// #3190: a dona vende pelo WhatsApp do próprio celular, então o canhoto da Casa sai do app, não da conexão do
/// sistema. O aparelho pede a imagem pelo id do pedido do app; a empresa vem sempre do pareamento.
/// </summary>
[Collection("MobileE2E")]
public class MobileCanhotoClienteE2ETests(MobileE2EFixture fixture)
{
    private static string Rota(string mobileOrderId) => $"/api/mobile/pedidos/{Uri.EscapeDataString(mobileOrderId)}/canhoto-cliente";

    [SkippableFact]
    public async Task PedidoSincronizado_DevolvePngComCodigoDoErp()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (empresaId, lojaId) = await fixture.SeedEmpresaELojaAsync();
        var aparelho = await fixture.SeedMobileDeviceAsync(empresaId, lojaId);
        var (mobileOrderId, _) = await SeedPedidoAsync(empresaId, lojaId);

        var resp = await fixture.CreateMobileClient(aparelho.ApiKey).GetAsync(Rota(mobileOrderId));

        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
        resp.Content.Headers.ContentType!.MediaType.Should().Be("image/png", "a imagem traz o código do ERP; o texto do app não leva outro");
        var bytes = await resp.Content.ReadAsByteArrayAsync();
        bytes.Take(4).Should().Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, "é a imagem do canhoto, não JSON");
        if (Environment.GetEnvironmentVariable("CANHOTO_3190_SALVAR") is { Length: > 0 } destino)
            await File.WriteAllBytesAsync(destino, bytes); // prova visual local, nunca no CI
    }

    [SkippableFact]
    public async Task PedidoAindaNaoSincronizado_409()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (empresaId, lojaId) = await fixture.SeedEmpresaELojaAsync();
        var aparelho = await fixture.SeedMobileDeviceAsync(empresaId, lojaId);

        var resp = await fixture.CreateMobileClient(aparelho.ApiKey).GetAsync(Rota("ord-" + Guid.NewGuid().ToString("N")));

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict, "o pedido só existe no ERP depois do sync");
    }

    [SkippableFact]
    public async Task SemChave_401_EAparelhoDeOutraEmpresa_NaoVeOPedido()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (empresaA, lojaA) = await fixture.SeedEmpresaELojaAsync();
        var (empresaB, lojaB) = await fixture.SeedEmpresaELojaAsync();
        var (mobileOrderId, _) = await SeedPedidoAsync(empresaA, lojaA);
        var deB = await fixture.SeedMobileDeviceAsync(empresaB, lojaB);

        (await fixture.CreateClient().GetAsync(Rota(mobileOrderId))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await fixture.CreateMobileClient(deB.ApiKey).GetAsync(Rota(mobileOrderId))).StatusCode
            .Should().Be(HttpStatusCode.Conflict, "para outra empresa o pedido não existe");
    }

    [SkippableFact]
    public async Task Aberto_GravaUmEventoSoPorPedido()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (empresaId, lojaId) = await fixture.SeedEmpresaELojaAsync();
        var aparelho = await fixture.SeedMobileDeviceAsync(empresaId, lojaId);
        var (mobileOrderId, pedidoId) = await SeedPedidoAsync(empresaId, lojaId);
        var http = fixture.CreateMobileClient(aparelho.ApiKey);

        (await http.PostAsync(Rota(mobileOrderId) + "/aberto", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await http.PostAsync(Rota(mobileOrderId) + "/aberto", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = fixture.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        using var semRls = db.UseRowLevelSecurityBypass();
        (await db.Set<PedidoEvento>().CountAsync(e => e.PedidoId == pedidoId && e.Tipo == CanhotoClienteDoApp.Marco))
            .Should().Be(1, "o evento mede a intenção de compartilhar, uma vez por pedido");
    }

    [SkippableFact]
    public async Task PedidoDaVitrine_SemIdDoApp_AchaPeloIdDoErp()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (empresaId, lojaId) = await fixture.SeedEmpresaELojaAsync();
        var aparelho = await fixture.SeedMobileDeviceAsync(empresaId, lojaId);
        var (_, pedidoId) = await SeedPedidoAsync(empresaId, lojaId, doApp: false);

        var resp = await fixture.CreateMobileClient(aparelho.ApiKey).GetAsync(Rota(pedidoId.ToString()));

        resp.StatusCode.Should().Be(HttpStatusCode.OK, "pedido da vitrine desce ao app com o id do ERP, sem id do app");
    }

    private async Task<(string MobileOrderId, Guid PedidoId)> SeedPedidoAsync(Guid empresaId, Guid lojaId, bool doApp = true)
    {
        using var scope = fixture.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        using var semRls = db.UseRowLevelSecurityBypass();
        var pedido = Pedido.Criar(empresaId, lojaId: lojaId, origem: "mobile");
        if (doApp) pedido.MobileOrderId = "ord-" + Guid.NewGuid().ToString("N");
        pedido.Itens.Add(new PedidoItem
        {
            Id = Guid.NewGuid(), PedidoId = pedido.Id, Nome = "Lasanha bolonhesa 800 g", Quantidade = 1,
            PrecoUnitario = 45m, Subtotal = 45m, CriadoEm = DateTime.UtcNow,
        });
        pedido.RecalcularTotal();
        db.Pedidos.Add(pedido);
        await db.SaveChangesAsync();
        return (pedido.MobileOrderId ?? "", pedido.Id);
    }
}

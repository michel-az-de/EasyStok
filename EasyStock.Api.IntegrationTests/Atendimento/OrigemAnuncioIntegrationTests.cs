using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EasyStock.Application.UseCases.FeatureFlags;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Api.IntegrationTests.Atendimento;

public sealed partial class PedidoConversaConcorrenciaTests
{
    /// <summary>
    /// #2928 S4: o pedido gerado a partir da conversa leva o anúncio que ela tinha (Postgres real, colunas do owned type).
    /// </summary>
    [SkippableFact]
    public async Task GerarPedido_ConversaDeAnuncio_PedidoSaiComAOrigem()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();
        using var client = factory.CreateClient();
        var cenario = await SemearAsync(factory);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(EmpresaId);
            var conversa = await db.AtendimentoConversas.SingleAsync(c => c.Id == cenario.ConversaId);
            conversa.RegistrarOrigemAnuncio(OrigemAnuncio.DoAnuncioWhatsApp("120001", "Brigadeiro de Natal", "ctwa-abc", DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        var resposta = await client.PostAsJsonAsync($"/api/atendimento/conversas/{cenario.ConversaId}/pedido", new
        {
            clienteId = cenario.ClienteId, vinculoRevisao = cenario.VinculoRevisao,
            itens = new[] { new { cardapioItemId = cenario.CardapioItemId, qtd = 1 } },
            janelaId = cenario.JanelaId,
            dataEntrega = cenario.DataEntrega.ToString("yyyy-MM-dd"),
            forma = "na_entrega",
        });

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        using var verificacao = factory.Services.CreateScope();
        var estado = verificacao.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        var pedido = await estado.Pedidos.IgnoreQueryFilters().SingleAsync(p => p.EmpresaId == EmpresaId && p.ClienteId == cenario.ClienteId);
        pedido.OrigemAnuncio.Should().NotBeNull();
        pedido.OrigemAnuncio!.Plataforma.Should().Be(OrigemAnuncio.PlataformaMeta);
        pedido.OrigemAnuncio.Via.Should().Be(OrigemAnuncio.ViaAnuncioWhatsApp);
        pedido.OrigemAnuncio.IdAnuncio.Should().Be("120001");
    }

    /// <summary>
    /// #2928 S4: <c>POST sessoes</c> aceita corpo ausente, <c>{}</c> ou com <c>campanha</c> (compatível com o widget
    /// antigo); a origem da sessão chega na conversa na primeira mensagem, sem o id de clique quando não há consentimento.
    /// </summary>
    [SkippableFact]
    public async Task ChatSite_AbrirSessaoComCorpoOpcional_OrigemChegaNaConversa()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();
        using var client = factory.CreateClient();
        const string slug = "chat-origem-2928";
        await SemearAsync(factory, slug);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(EmpresaId);
            db.TenantFeatureFlags.AddRange(
                TenantFeatureFlag.Criar(EmpresaId, FeatureCatalogo.ModuloAtendimento, true, "teste@local.invalid"),
                TenantFeatureFlag.Criar(EmpresaId, FeatureCatalogo.CanalChatSite, true, "teste@local.invalid"));
            await db.SaveChangesAsync();
        }

        (await client.PostAsync($"/api/public/chat/{slug}/sessoes", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync($"/api/public/chat/{slug}/sessoes", new { })).StatusCode.Should().Be(HttpStatusCode.OK);
        var abrir = await client.PostAsJsonAsync($"/api/public/chat/{slug}/sessoes", new
        {
            campanha = new Dictionary<string, object>
            {
                ["utm_source"] = "google", ["utm_medium"] = "cpc", ["utm_campaign"] = "natal", ["gclid"] = "abc123", ["marketing"] = false,
            },
        });
        abrir.StatusCode.Should().Be(HttpStatusCode.OK);
        using var aberta = JsonDocument.Parse(await abrir.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Add("X-Chat-Token", aberta.RootElement.GetProperty("data").GetProperty("token").GetString());

        (await client.PostAsJsonAsync($"/api/public/chat/{slug}/mensagens", new { texto = "Oi, vi o anúncio" })).StatusCode
            .Should().Be(HttpStatusCode.OK);

        using var verificacao = factory.Services.CreateScope();
        var estado = verificacao.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        estado.SetMobileTenantContext(EmpresaId);
        var conversa = await estado.AtendimentoConversas.SingleAsync(c => c.Canal == CanalConversa.ChatSite);
        conversa.OrigemAnuncio.Should().NotBeNull();
        conversa.OrigemAnuncio!.Plataforma.Should().Be(OrigemAnuncio.PlataformaGoogle);
        conversa.OrigemAnuncio.Via.Should().Be(OrigemAnuncio.ViaSiteChat);
        conversa.OrigemAnuncio.Campanha.Should().Be("natal");
        conversa.OrigemAnuncio.IdClique.Should().BeNull("sem marketing=true o id de clique é descartado");
    }
}

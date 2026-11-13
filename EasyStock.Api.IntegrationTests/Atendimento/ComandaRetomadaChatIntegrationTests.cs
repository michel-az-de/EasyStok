using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Api.IntegrationTests.Atendimento;

public sealed partial class PedidoConversaConcorrenciaTests
{
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Comanda_RetomaChat_SemDuplicarConversaOuPerderSelecao(bool reutilizar)
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory(Provedor(new()));
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory, "chat-comanda");
        var conversaId = await PrepararChatComanda(factory, client, c.ClienteId);
        var original = client.DefaultRequestHeaders.GetValues("X-Chat-Token").Single();
        var link = await Link(factory, conversaId);
        var estado = await Estado(client, link);
        estado.ChatSite.Should().BeTrue();
        await Salvar(client, link, estado, estado.Rascunho with { Itens = [new(c.CardapioItemId, 2)], Observacoes = "Contexto preservado" });
        var antes = await Estado(client, link);
        if (!reutilizar) client.DefaultRequestHeaders.Remove("X-Chat-Token");
        var resposta = await client.PostAsync(Rota(link) + "/chat", null);
        resposta.StatusCode.Should().Be(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        var token = json.RootElement.GetProperty("token").GetString()!;
        json.RootElement.GetProperty("conversaId").GetGuid().Should().Be(conversaId);
        if (reutilizar) token.Should().Be(original); else token.Should().NotBe(original);
        client.DefaultRequestHeaders.Remove("X-Chat-Token");
        client.DefaultRequestHeaders.Add("X-Chat-Token", token);
        client.DefaultRequestHeaders.Add("X-Chat-Conversa", conversaId.ToString());
        (await client.PostAsJsonAsync("/api/public/chat/chat-comanda/mensagens", new { texto = "Comanda retomada na mesma conversa" })).StatusCode.Should().Be(HttpStatusCode.OK);
        var historico = await client.GetStringAsync("/api/public/chat/chat-comanda/mensagens");
        historico.Should().Contain("Quero montar meu pedido").And.Contain("Comanda retomada na mesma conversa");
        var depois = await Estado(client, link);
        depois.Revisao.Should().Be(antes.Revisao);
        depois.Rascunho.Should().BeEquivalentTo(antes.Rascunho);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        (await db.AtendimentoConversas.CountAsync(x => x.Canal == CanalConversa.ChatSite)).Should().Be(1);
        var conversa = await db.AtendimentoConversas.SingleAsync(x => x.Id == conversaId);
        conversa.Encerrar(DateTime.UtcNow);
        await db.SaveChangesAsync();
        (await client.PostAsJsonAsync("/api/public/chat/chat-comanda/mensagens", new { texto = "Nao abrir outra conversa" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsync(Rota(link) + "/chat", null)).StatusCode.Should().Be(HttpStatusCode.Gone);
        (await db.AtendimentoConversas.CountAsync(x => x.Canal == CanalConversa.ChatSite)).Should().Be(1);
    }

    [SkippableFact]
    public async Task Comanda_RetomaChat_NaoReaproveitaSessaoDeOutraConversa()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory(Provedor(new()));
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory, "chat-comanda");
        var conversaId = await PrepararChatComanda(factory, client, c.ClienteId);
        var link = await Link(factory, conversaId);
        using var outro = factory.CreateClient();
        var abrir = await outro.PostAsync("/api/public/chat/chat-comanda/sessoes", null);
        using var sessao = JsonDocument.Parse(await abrir.Content.ReadAsStringAsync());
        var alheio = sessao.RootElement.GetProperty("data").GetProperty("token").GetString()!;
        outro.DefaultRequestHeaders.Add("X-Chat-Token", alheio);
        (await outro.PostAsJsonAsync("/api/public/chat/chat-comanda/mensagens", new { texto = "Conversa alheia" })).StatusCode.Should().Be(HttpStatusCode.OK);
        var retomar = await outro.PostAsync(Rota(link) + "/chat", null);
        retomar.StatusCode.Should().Be(HttpStatusCode.OK);
        using var retomada = JsonDocument.Parse(await retomar.Content.ReadAsStringAsync());
        retomada.RootElement.GetProperty("token").GetString().Should().NotBe(alheio);
        retomada.RootElement.GetProperty("conversaId").GetGuid().Should().Be(conversaId);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        (await db.AtendimentoConversas.CountAsync(x => x.Canal == CanalConversa.ChatSite)).Should().Be(2);
    }

    [SkippableFact]
    public async Task Comanda_RetomaChat_RecusaLinkInvalidoOuConversaDeOutroCanal()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory(Provedor(new()));
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory, "chat-comanda");
        var link = await Link(factory, c.ConversaId);
        (await Estado(client, link)).ChatSite.Should().BeFalse();
        (await client.PostAsync(Rota(link) + "/chat", null)).StatusCode.Should().Be(HttpStatusCode.Gone);
        (await client.PostAsync(Rota("token-inexistente-00000000000000000000") + "/chat", null)).StatusCode.Should().Be(HttpStatusCode.Gone);
    }
}

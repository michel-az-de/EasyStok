using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using EasyStock.Api.IntegrationTests.Helpers;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Storefront;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Api.IntegrationTests.Atendimento;

/// <summary>
/// #1725-7 e #1725-8: quantos comandos SQL a lista de comandas do console, a leitura da comanda, o rascunho e as
/// janelas mandam ao banco. A conta não pode crescer com o número de links da conversa nem com o número de pratos.
/// Comandos do EF (sem o SET/RESET do tenant), antes e depois: lista com 1 link 33 e 23, com 3 links 95 e 25; leitura
/// da comanda paga 33 e 23; rascunho com 1 prato 76 e 57, com 10 pratos 94 e 57; janelas com 1 prato 10 e 10, com 10
/// pratos 28 e 10.
/// </summary>
public sealed partial class PedidoConversaConcorrenciaTests
{
    private const int PratosExtras = 9;

    [SkippableFact]
    public async Task Comanda_ListaLeituraRascunhoEJanelas_ConsultasNaoCrescemComLinksNemPratos()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = ContadorDeComandosSql.Instalar(CriarFactory(Provedor(new ConcurrentDictionary<string, CriarPreferenceCommand>())));
        using var client = factory.CreateClient();
        var contador = factory.Services.GetRequiredService<ContadorDeComandosSql>();
        var c = await SemearAsync(factory);
        var pratos = await SemearPratosAsync(factory, PratosExtras);
        var lista = $"/api/atendimento/conversas/{c.ConversaId}/comandas-cliente";
        const string comanda = "/api/storefront/cardapio-conversa";

        var tokenPai = await Link(factory, c.ConversaId);
        var pai = await Estado(client, tokenPai);
        await Salvar(client, tokenPai, pai, pai.Rascunho with { Itens = [new(c.CardapioItemId, 1)],
            JanelaId = c.JanelaId, DataEntrega = c.DataEntrega });
        pai = await Estado(client, tokenPai);
        var pedidoId = (await Enviar(client, tokenPai, pai.Revisao)).PedidoId;
        await ConfirmarPagamento(factory, pedidoId, "172507", 15);

        var listaComUmLink = await contador.ContarAsync(lista, () => Listar(client, lista, 1));
        var leitura = await contador.ContarAsync(comanda, () => Estado(client, tokenPai));

        using var novaEdicao = await client.PostAsync($"{Rota(tokenPai)}/nova-edicao", null);
        novaEdicao.StatusCode.Should().Be(HttpStatusCode.OK, await novaEdicao.Content.ReadAsStringAsync());
        var filho = (await novaEdicao.Content.ReadFromJsonAsync<LinkCardapioConversaGerado>())!;
        await Link(factory, c.ConversaId);
        var listaComTresLinks = await contador.ContarAsync(lista, () => Listar(client, lista, 3));

        var estadoFilho = await Estado(client, filho.Token);
        var rascunhoComUmPrato = await contador.ContarAsync(comanda, () => Salvar(client, filho.Token, estadoFilho,
            estadoFilho.Rascunho with { Itens = [new(c.CardapioItemId, 2)] }));
        estadoFilho = await Estado(client, filho.Token);
        var rascunhoComDezPratos = await contador.ContarAsync(comanda, () => Salvar(client, filho.Token, estadoFilho,
            estadoFilho.Rascunho with { Itens = [new(c.CardapioItemId, 2), .. pratos.Select(p => new ItemPedidoCheckout(p, 1))] }));
        (await Estado(client, filho.Token)).Selecao.Should().HaveCount(PratosExtras + 1);

        var janelasComUmPrato = await contador.ContarAsync(comanda, () => Janelas(client, filho.Token, [c.CardapioItemId]));
        var janelasComDezPratos = await contador.ContarAsync(comanda, () => Janelas(client, filho.Token, [c.CardapioItemId, .. pratos]));

        var medida = $"lista 1 link {listaComUmLink}, 3 links {listaComTresLinks}; leitura {leitura}; rascunho 1 prato " +
            $"{rascunhoComUmPrato}, 10 pratos {rascunhoComDezPratos}; janelas 1 prato {janelasComUmPrato}, 10 pratos {janelasComDezPratos}";
        listaComTresLinks.Should().BeLessThanOrEqualTo(listaComUmLink + 2 * 2,
            $"cada link a mais custa no máximo 2 consultas, não uma leitura inteira da comanda ({medida})");
        rascunhoComDezPratos.Should().Be(rascunhoComUmPrato, $"os pratos vêm numa consulta só ({medida})");
        janelasComDezPratos.Should().Be(janelasComUmPrato, $"os pratos vêm numa consulta só ({medida})");
        leitura.Should().BeLessThanOrEqualTo(25, $"a comanda com pedido pago não relê o pedido ({medida})");
    }

    private static async Task<IReadOnlyList<Guid>> SemearPratosAsync(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory, int quantos)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        db.SetMobileTenantContext(EmpresaId);
        var loja = await db.Storefronts.SingleAsync(s => s.EmpresaId == EmpresaId);
        var pratos = Enumerable.Range(1, quantos).Select(i =>
        {
            var prato = CardapioItem.CriarAvulso(loja.Id, $"Prato {i}", 10m + i);
            prato.TornarVisivel();
            return prato;
        }).ToList();
        db.CardapioItens.AddRange(pratos);
        await db.SaveChangesAsync();
        return pratos.Select(p => p.Id).ToList();
    }

    private static async Task Listar(HttpClient client, string rota, int links)
    {
        using var resposta = await client.GetAsync(rota);
        resposta.StatusCode.Should().Be(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync());
        using var json = System.Text.Json.JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("data").GetArrayLength().Should().Be(links);
    }

    private static async Task Janelas(HttpClient client, string token, IEnumerable<Guid> itens)
    {
        using var resposta = await client.GetAsync($"{Rota(token)}/janelas?{string.Join('&', itens.Select(i => $"itens={i}"))}");
        resposta.StatusCode.Should().Be(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync());
    }
}

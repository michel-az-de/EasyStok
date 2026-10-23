using System.Net;
using System.Net.Http.Json;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Api.IntegrationTests.Atendimento;

/// <summary>#2002 (banca de 23/10): o que o cardápio deixa vender na comanda e o total que o cliente confirma.</summary>
public sealed partial class PedidoConversaConcorrenciaTests
{
    // comanda-02: o cliente pediu 2 brigadeiros; o prato esgotou; ele acrescenta uma coxinha. Salvar, enviar e o aceite
    // validavam TODAS as linhas contra "disponível", inclusive os 2 que já estavam no pedido: 422 e a alteração presa.
    [SkippableFact]
    public async Task Alteracao_PratoQueEsgotouDepoisDoPedido_NaoTravaOAcrescimoDeOutroPrato()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory(Provedor(new()));
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory);
        var token = await Link(factory, c.ConversaId);
        var estado = await Estado(client, token);
        await Salvar(client, token, estado, estado.Rascunho with { Itens = [new(c.CardapioItemId, 2)], JanelaId = c.JanelaId, DataEntrega = c.DataEntrega });
        (await Enviar(client, token, 1)).Situacao.Should().Be("aguardando_pagamento");
        Guid coxinhaId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(EmpresaId);
            var brigadeiro = await db.CardapioItens.SingleAsync(i => i.Id == c.CardapioItemId);
            brigadeiro.MarcarEsgotado();
            var coxinha = CardapioItem.CriarAvulso(brigadeiro.StorefrontId, "Coxinha", 6m);
            coxinha.TornarVisivel();
            db.CardapioItens.Add(coxinha);
            await db.SaveChangesAsync();
            coxinhaId = coxinha.Id;
        }
        var ajuste = await Link(factory, c.ConversaId);
        estado = await Estado(client, ajuste);

        await Salvar(client, ajuste, estado, estado.Rascunho with { Itens = [new(c.CardapioItemId, 2), new(coxinhaId, 1)] });
        var envio = await Enviar(client, ajuste, 1);

        envio.Total.Should().Be(31m, "2 × 10 + 6 + frete 5: a alteração só de acréscimo foi aceita sozinha");
        var final = await Estado(client, ajuste);
        final.Pedido!.Itens.Select(i => (i.CardapioItemId, i.Quantidade)).Should().BeEquivalentTo(
            new[] { ((Guid?)c.CardapioItemId, 2m), ((Guid?)coxinhaId, 1m) });
    }

    // comanda-02: aumentar a quantidade do prato esgotado continua barrado (o que o pedido não reservou).
    [SkippableFact]
    public async Task Alteracao_AumentarAQuantidadeDoPratoEsgotado_SegueRecusado()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory(Provedor(new()));
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory);
        var token = await Link(factory, c.ConversaId);
        var estado = await Estado(client, token);
        await Salvar(client, token, estado, estado.Rascunho with { Itens = [new(c.CardapioItemId, 2)], JanelaId = c.JanelaId, DataEntrega = c.DataEntrega });
        await Enviar(client, token, 1);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(EmpresaId);
            (await db.CardapioItens.SingleAsync(i => i.Id == c.CardapioItemId)).MarcarEsgotado();
            await db.SaveChangesAsync();
        }
        var ajuste = await Link(factory, c.ConversaId);
        estado = await Estado(client, ajuste);

        var resposta = await client.PutAsJsonAsync($"{Rota(ajuste)}/rascunho",
            new { estado.Revisao, Rascunho = estado.Rascunho with { Itens = [new(c.CardapioItemId, 3)] } });

        resposta.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, await resposta.Content.ReadAsStringAsync());
        (await resposta.Content.ReadAsStringAsync()).Should().Contain("indisponível");
    }

    // comanda-03: a dona esconde a categoria; quem já tinha o prato no rascunho (link vale 24 h) seguia podendo enviar e pagar.
    [SkippableFact]
    public async Task Comanda_PratoDeCategoriaEscondida_NaoSalvaNemEnvia()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory(Provedor(new()));
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory);
        var token = await Link(factory, c.ConversaId);
        var estado = await Estado(client, token);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(EmpresaId);
            var brigadeiro = await db.CardapioItens.SingleAsync(i => i.Id == c.CardapioItemId);
            var secao = CardapioSecao.CriarRaiz(brigadeiro.StorefrontId, "Páscoa", 1);
            secao.AlterarVisibilidade(false);
            db.Add(secao);
            brigadeiro.DefinirSecao(secao.Id);
            await db.SaveChangesAsync();
        }

        var resposta = await client.PutAsJsonAsync($"{Rota(token)}/rascunho",
            new { estado.Revisao, Rascunho = estado.Rascunho with { Itens = [new(c.CardapioItemId, 1)], JanelaId = c.JanelaId, DataEntrega = c.DataEntrega } });

        resposta.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, await resposta.Content.ReadAsStringAsync());
        (await resposta.Content.ReadAsStringAsync()).Should().Contain("indisponível");
    }

    // comanda-12: o pedido tem uma linha que a casa lançou à mão (PWA: produto, sem item de cardápio). O total que o cliente
    // vê antes de enviar tem de ser o do aceite; antes, a linha entrava no aceite como "frete" e não no total proposto.
    [SkippableFact]
    public async Task Alteracao_LinhaLancadaPelaCasaEntraNoTotalProposto_ENoAceite()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory(Provedor(new()));
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory);
        var token = await Link(factory, c.ConversaId);
        var estado = await Estado(client, token);
        await Salvar(client, token, estado, estado.Rascunho with { Itens = [new(c.CardapioItemId, 1)], JanelaId = c.JanelaId, DataEntrega = c.DataEntrega });
        var original = await Enviar(client, token, 1);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(EmpresaId);
            var agora = DateTime.UtcNow;
            var produtoId = Guid.NewGuid();
            var categoriaId = Guid.NewGuid();
            db.Set<Categoria>().Add(new Categoria { Id = categoriaId, EmpresaId = EmpresaId, Nome = "Extras", CriadoEm = agora, AlteradoEm = agora });
            db.Set<Produto>().Add(new Produto { Id = produtoId, EmpresaId = EmpresaId, CategoriaId = categoriaId, Nome = "Vela", Status = StatusProduto.Ativo, CriadoEm = agora, AlteradoEm = agora });
            var pedido = await db.Pedidos.Include(p => p.Itens).SingleAsync(p => p.Id == original.PedidoId);
            // Add explícito: com Guid na mão, o EF trataria a linha achada pela navegação do pedido como já existente.
            db.Add(new PedidoItem { Id = Guid.NewGuid(), PedidoId = pedido.Id, ProdutoId = produtoId, Nome = "Vela de aniversário",
                Quantidade = 1, PrecoUnitario = 3m, Subtotal = 3m, CriadoEm = agora });
            pedido.Total = EasyStock.Domain.ValueObjects.Dinheiro.FromDecimal(pedido.Total.Valor + 3m);
            await db.SaveChangesAsync();
            pedido.Total.Valor.Should().Be(18m, "brigadeiro 10 + frete 5 + vela 3");
        }
        var ajuste = await Link(factory, c.ConversaId);
        estado = await Estado(client, ajuste);
        await Salvar(client, ajuste, estado, estado.Rascunho with { Itens = [new(c.CardapioItemId, 2)] });

        var previsto = (await Estado(client, ajuste)).TotalPrevisto;
        var envio = await Enviar(client, ajuste, 1);

        previsto.Should().Be(28m, "2 × 10 + frete 5 + vela 3: o que o pedido mantém fora dos pratos");
        envio.Total.Should().Be(previsto, "o cliente confirma o valor que viu, e o pedido fica nele");
    }
}

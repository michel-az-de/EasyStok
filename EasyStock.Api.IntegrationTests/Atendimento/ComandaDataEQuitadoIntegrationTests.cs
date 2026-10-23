using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Api.IntegrationTests.Atendimento;

/// <summary>Banca de tela real de 23/10 (frente E): data da alteração da comanda e retrato do link depois de quitar.</summary>
public sealed partial class PedidoConversaConcorrenciaTests
{
    // E10: o cliente trocou a data pelo link da comanda e somou itens; o envio voltou 200 com o total novo, mas o pedido
    // ficou na data antiga e o link novo vinha sem janela nem data. A alteração aprovada perdia a data em silêncio.
    [SkippableFact]
    public async Task Alteracao_TrocaDeDataPeloLinkEntraNoPedido()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory(Provedor(new()));
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory);
        var token = await Link(factory, c.ConversaId);
        var estado = await Estado(client, token);
        await Salvar(client, token, estado, estado.Rascunho with { Itens = [new(c.CardapioItemId, 1)], JanelaId = c.JanelaId, DataEntrega = c.DataEntrega });
        var envio = await Enviar(client, token, 1);

        var ajuste = await Link(factory, c.ConversaId);
        estado = await Estado(client, ajuste);
        estado.Rascunho.JanelaId.Should().Be(c.JanelaId, "o link novo mostra a janela do pedido");
        estado.Rascunho.DataEntrega.Should().Be(c.DataEntrega, "o link novo mostra a data do pedido");
        var novaData = c.DataEntrega.AddDays(7);
        await Salvar(client, ajuste, estado, estado.Rascunho with { Itens = [new(c.CardapioItemId, 2)], DataEntrega = novaData });
        var alteracao = await Enviar(client, ajuste, 1);
        if (alteracao.Situacao == "alteracao_aguardando_confirmacao")
        {
            var aceite = await client.PostAsJsonAsync(
                $"/api/atendimento/conversas/{c.ConversaId}/comandas-cliente/{estado.LinkId}/confirmar", new { Aceitar = true });
            aceite.StatusCode.Should().Be(HttpStatusCode.OK, await aceite.Content.ReadAsStringAsync());
        }

        var final = await Estado(client, ajuste);
        final.Pedido!.PedidoId.Should().Be(envio.PedidoId);
        final.Pedido.Itens.Should().ContainSingle().Which.Quantidade.Should().Be(2);
        final.Pedido.Janela!.Data.Should().Be(novaData, "a data escolhida pelo cliente não some");
    }

    // E10: data sem janela disponível não some calada: os itens entram e a equipe é chamada na conversa.
    [SkippableFact]
    public async Task Alteracao_DataSemJanelaAvisaAEquipe()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory(Provedor(new()));
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory);
        var token = await Link(factory, c.ConversaId);
        var estado = await Estado(client, token);
        await Salvar(client, token, estado, estado.Rascunho with { Itens = [new(c.CardapioItemId, 1)], JanelaId = c.JanelaId, DataEntrega = c.DataEntrega });
        await Enviar(client, token, 1);
        var ajuste = await Link(factory, c.ConversaId);
        estado = await Estado(client, ajuste);
        var semJanela = c.DataEntrega.AddDays(1); // a janela da semente só abre no dia da semana da entrega
        await Salvar(client, ajuste, estado, estado.Rascunho with { Itens = [new(c.CardapioItemId, 2)], DataEntrega = semJanela });
        (await Enviar(client, ajuste, 1)).Situacao.Should().Be("alteracao_aguardando_confirmacao", "troca de data passa pela equipe");

        var aceite = await client.PostAsJsonAsync(
            $"/api/atendimento/conversas/{c.ConversaId}/comandas-cliente/{estado.LinkId}/confirmar", new { Aceitar = true });

        aceite.StatusCode.Should().Be(HttpStatusCode.OK, await aceite.Content.ReadAsStringAsync());
        var final = await Estado(client, ajuste);
        final.Pedido!.Itens.Should().ContainSingle().Which.Quantidade.Should().Be(2);
        final.Pedido.Janela!.Data.Should().Be(c.DataEntrega);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        db.SetMobileTenantContext(EmpresaId);
        (await db.AtendimentoMensagens.Select(m => m.Texto).ToListAsync())
            .Should().Contain(t => t != null && t.Contains($"entrega em {semJanela:dd/MM}"), "a equipe é chamada para combinar a data");
    }

    // E11: depois de o complemento da alteração ser pago, a comanda pública seguia com "aguardando_pagamento", o saldo
    // e o link antigo do complemento.
    [SkippableFact]
    public async Task Alteracao_ComplementoPagoMostraOPedidoQuitadoSemLink()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        var preferencias = new ConcurrentDictionary<string, CriarPreferenceCommand>();
        await using var factory = CriarFactory(Provedor(preferencias));
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory);
        var token = await Link(factory, c.ConversaId);
        var estado = await Estado(client, token);
        await Salvar(client, token, estado, estado.Rascunho with { Itens = [new(c.CardapioItemId, 1)], JanelaId = c.JanelaId, DataEntrega = c.DataEntrega });
        var envio = await Enviar(client, token, 1);
        await ConfirmarPagamento(factory, envio.PedidoId, "500001", 15);

        var ajuste = await Link(factory, c.ConversaId);
        estado = await Estado(client, ajuste);
        await Salvar(client, ajuste, estado, estado.Rascunho with { Itens = [new(c.CardapioItemId, 2)] });
        var complemento = await Enviar(client, ajuste, 1);
        complemento.Situacao.Should().Be("aguardando_pagamento");
        complemento.Saldo.Should().Be(10);
        await ConfirmarPagamento(factory, envio.PedidoId, "500002", 10);

        var quitado = await Estado(client, ajuste);
        quitado.Pedido!.TotalPago.Should().Be(25);
        quitado.Envio!.Situacao.Should().Be("pedido_confirmado");
        quitado.Envio.Saldo.Should().Be(0);
        quitado.Envio.LinkPagamento.Should().BeNull("o link do complemento pago não é mais oferecido");
    }
}

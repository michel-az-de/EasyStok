using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Sales;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Api.IntegrationTests.Atendimento;

public sealed partial class PedidoConversaConcorrenciaTests
{
    /// <summary>
    /// #1883 (#1902): o JSON do KDS e da agenda traz <c>numeroComanda</c>, o número da conversa que gerou o
    /// pedido, e <c>numeroCurto</c> segue como referência. Pedido sem conversa vem com o campo nulo.
    /// </summary>
    [SkippableFact]
    public async Task KdsEAgenda_DevolvemNumeroComandaDaConversaNoJson()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory);
        var criado = await client.PostAsJsonAsync($"/api/atendimento/conversas/{c.ConversaId}/pedido", new
        {
            itens = new[] { new { cardapioItemId = c.CardapioItemId, qtd = 1 } },
            janelaId = c.JanelaId, dataEntrega = c.DataEntrega.ToString("yyyy-MM-dd"), forma = "na_entrega",
        });
        criado.StatusCode.Should().Be(HttpStatusCode.OK, await criado.Content.ReadAsStringAsync());

        long numeroComanda;
        Guid pedidoId;
        var semConversa = new Pedido
        {
            Id = Guid.NewGuid(), EmpresaId = EmpresaId, Status = StatusPedidoMapper.Aguardando, ClienteNome = "Sem conversa",
            Total = Dinheiro.FromDecimal(10m), CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow,
        };
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(EmpresaId);
            var conversa = await db.AtendimentoConversas.SingleAsync(x => x.Id == c.ConversaId);
            numeroComanda = conversa.NumeroComanda;
            pedidoId = conversa.PedidoEmAndamentoId!.Value;
            db.Pedidos.Add(semConversa);
            db.VagasOcupadas.Add(VagaOcupada.Ocupar(c.JanelaId, c.DataEntrega, semConversa.Id));
            await db.SaveChangesAsync();
        }
        numeroComanda.Should().BePositive("a conversa recebe o número da comanda ao ser gravada");
        var dia = c.DataEntrega.ToString("yyyy-MM-dd");

        // A mesma lista que o painel de Entregas do console pede para o dia (`STATUS_KDS_DIA`): a encomenda com
        // sinal nasce em aguardando_pagamento e já aparece lá.
        const string statusDoDia = "aguardando_pagamento,aguardando_aprovacao_baba,aprovado_baba,aguardando,preparando,pronto,saiu_para_entrega,entregue";
        using var kds = await client.GetAsync($"/api/kds/pedidos?status={statusDoDia}&data={dia}");
        var kdsCorpo = await kds.Content.ReadAsStringAsync();
        kds.StatusCode.Should().Be(HttpStatusCode.OK, kdsCorpo);
        using var kdsJson = JsonDocument.Parse(kdsCorpo);
        var cards = kdsJson.RootElement.GetProperty("data").EnumerateArray().ToDictionary(e => e.GetProperty("id").GetGuid());
        cards.Should().ContainKey(pedidoId, kdsCorpo).And.ContainKey(semConversa.Id, kdsCorpo);
        cards[pedidoId].GetProperty("numeroComanda").GetInt64().Should().Be(numeroComanda);
        cards[pedidoId].GetProperty("numeroCurto").GetString().Should().Be(pedidoId.ToString("N")[..8].ToUpperInvariant());
        cards[semConversa.Id].GetProperty("numeroComanda").ValueKind.Should().Be(JsonValueKind.Null);

        using var agenda = await client.GetAsync($"/api/atendimento/agenda?de={dia}&ate={dia}");
        var agendaCorpo = await agenda.Content.ReadAsStringAsync();
        agenda.StatusCode.Should().Be(HttpStatusCode.OK, agendaCorpo);
        using var agendaJson = JsonDocument.Parse(agendaCorpo);
        var pedidos = agendaJson.RootElement.GetProperty("data").GetProperty("dias").EnumerateArray()
            .SelectMany(d => d.GetProperty("janelas").EnumerateArray())
            .SelectMany(j => j.GetProperty("pedidos").EnumerateArray())
            .ToDictionary(p => p.GetProperty("pedidoId").GetGuid());
        pedidos.Should().ContainKey(pedidoId, agendaCorpo).And.ContainKey(semConversa.Id, agendaCorpo);
        pedidos[pedidoId].GetProperty("numeroComanda").GetInt64().Should().Be(numeroComanda);
        pedidos[pedidoId].GetProperty("numeroCurto").GetString().Should().Be(pedidoId.ToString("N")[..8].ToUpperInvariant());
        pedidos[semConversa.Id].GetProperty("numeroComanda").ValueKind.Should().Be(JsonValueKind.Null);
    }
}

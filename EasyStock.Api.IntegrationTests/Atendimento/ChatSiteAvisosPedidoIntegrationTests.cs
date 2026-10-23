using System.Collections.Concurrent;
using System.Text.Json;
using EasyStock.Application.Events.Pedidos;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Domain.Integration;
using EasyStock.Domain.Sales;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Api.IntegrationTests.Atendimento;

public sealed partial class PedidoConversaConcorrenciaTests
{
    /// <summary>
    /// #1991-1: o cliente do chat do site não recebia aviso nenhum do pedido (os avisos S13 só saíam pelo WhatsApp).
    /// Postgres real: o pedido da comanda do chat recebe o aviso de sinal pago (no próprio pagamento) e o de saiu para
    /// entrega (evento de integração despachado duas vezes), cada um uma vez, no chat público do visitante, sem
    /// <c>EventoNotificacao</c> de WhatsApp para eles.
    /// </summary>
    [SkippableFact]
    public async Task ChatSite_AvisosDoPedidoChegamNoChatDoVisitanteUmaVez()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        var preferencias = new ConcurrentDictionary<string, CriarPreferenceCommand>();
        await using var factory = CriarFactory(Provedor(preferencias));
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory, "chat-comanda");
        var conversaId = await PrepararChatComanda(factory, client, c.ClienteId);
        await PrepararAvisos(factory, conversaId);
        var token = await Link(factory, conversaId);
        var estado = await Estado(client, token);
        await Salvar(client, token, estado, estado.Rascunho with
        { Itens = [new(c.CardapioItemId, 1)], JanelaId = c.JanelaId, DataEntrega = c.DataEntrega });
        var envio = await Enviar(client, token, (await Estado(client, token)).Revisao);
        var antes = (await PublicasChatComanda(client)).Length;

        var sinal = preferencias.Values.Should().ContainSingle().Subject.ValorTotal;
        await ConfirmarPagamento(factory, envio.PedidoId, "chat-sinal-1", sinal);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(EmpresaId);
            var json = JsonSerializer.Serialize(new PedidoMudouStatusEvent(envio.PedidoId, EmpresaId, null,
                    StatusPedidoMapper.Pronto, StatusPedidoMapper.SaiuParaEntrega, "web", null, "Dona", DateTime.UtcNow),
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            db.OutboxEventosIntegracao.Add(OutboxEventoIntegracao.Criar(EmpresaId, "pedido.mudou_status", "pedido", envio.PedidoId, json));
            db.OutboxEventosIntegracao.Add(OutboxEventoIntegracao.Criar(EmpresaId, "pedido.mudou_status", "pedido", envio.PedidoId, json));
            await db.SaveChangesAsync();
        }
        await DespacharAvisos(factory);
        await DespacharAvisos(factory);

        var novas = (await PublicasChatComanda(client)).Skip(antes).Select(m => m.GetProperty("texto").GetString()!).ToList();
        novas.Should().ContainSingle(t => t.Contains("Recebemos o sinal de"), "sinal pago avisa no chat");
        novas.Should().ContainSingle(t => t.Contains("saiu para entrega"), "o evento repetido não repete o aviso");

        using var verificacao = factory.Services.CreateScope();
        var banco = verificacao.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        banco.SetMobileTenantContext(EmpresaId);
        (await banco.AtendimentoMensagens.CountAsync(m => m.ConversaId == conversaId
            && m.ExternoId!.StartsWith(AvisoPedidoNaConversa.PrefixoIdExterno))).Should().Be(2);
        (await banco.NotifEventos.CountAsync(e => e.RefEntidadeId == envio.PedidoId
            && (e.Tipo == TipoEventoNotificacao.PedidoSaiuParaEntrega || e.Tipo == TipoEventoNotificacao.ComandaAtualizada)))
            .Should().Be(0, "no chat do site o aviso não vai para o WhatsApp");
    }
}

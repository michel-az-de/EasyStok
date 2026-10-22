using System.Net;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Enums.Pagamentos;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EasyStock.Api.IntegrationTests.Atendimento;

public sealed partial class PedidoConversaConcorrenciaTests
{
    /// <summary>
    /// #1938: a operadora confere o pagamento e, entre a leitura da conferência e a confirmação, o webhook do
    /// Mercado Pago confirma o MESMO pagamento em outro escopo. A confirmação da conferência não pode reaproveitar
    /// as instâncias que o EF já rastreava (cobrança "pendente" velha com o PedidoPagamento novo): isso caía em
    /// "pagamento duplicado" e estornava o único pagamento do cliente.
    /// </summary>
    [SkippableFact]
    public async Task ConferirEnquantoOWebhookConfirmaOMesmoPagamento_NaoEstornaNada()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        var mp = Provedor(new());
        mp.ObterContaRecebedoraAsync(Arg.Any<CancellationToken>()).Returns("conta-1938");
        // Estorno que o provedor aceitaria: sem isto a falha apareceria como 500, não como o dinheiro devolvido.
        mp.EstornarAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new EstornoMercadoPagoResult("estorno-1938", 100m, "approved", "pagamento-1938", DateTime.UtcNow));
        await using var factory = CriarFactory(mp);
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory);
        Guid pedidoId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(EmpresaId);
            var pedido = Pedido.Criar(EmpresaId, origem: "whatsapp");
            pedido.ClienteId = c.ClienteId;
            pedido.Itens.Add(new PedidoItem
            {
                Id = Guid.NewGuid(), PedidoId = pedido.Id, Nome = "Pedido da corrida",
                Quantidade = 1, PrecoUnitario = 100m, Subtotal = 100m, CriadoEm = DateTime.UtcNow,
            });
            pedido.RecalcularTotal();
            db.Pedidos.Add(pedido);
            pedidoId = pedido.Id;
            db.CobrancasPedido.Add(CobrancaPedido.CriarOnline(EmpresaId, pedidoId, 100m,
                "preferencia-1938", "https://pagamento-teste.invalid/1938", DateTime.UtcNow.AddMinutes(30),
                1, DateTime.UtcNow, contaRecebedoraId: "conta-1938"));
            await db.SaveChangesAsync();
        }

        var aprovado = new PagamentoMercadoPago("pagamento-1938", "approved", null, pedidoId.ToString(),
            100m, DateTime.UtcNow, "pix", "bank_transfer", CollectorId: "conta-1938");
        // A consulta da conferência acontece depois de ela já ter lido pedido e cobranças. Nesse instante o
        // webhook chega por outro escopo (outro DbContext) e confirma o mesmo pagamento.
        mp.BuscarPagamentosPorReferenciaAsync(pedidoId.ToString(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                using var webhook = factory.Services.CreateScope();
                var confirmar = webhook.ServiceProvider.GetRequiredService<ConfirmarPagamentoPedidoUseCase>();
                var r = await confirmar.ExecuteAsync(new ConfirmarPagamentoPedidoInput(
                    pedidoId, aprovado.Id, aprovado.Status, aprovado.TransactionAmount,
                    aprovado.PaymentMethodId, aprovado.PaymentTypeId, aprovado.DateApproved, ContaRecebedoraId: aprovado.CollectorId));
                r.Confirmado.Should().BeTrue("o webhook confirma primeiro");
                return (IReadOnlyList<PagamentoMercadoPago>)[aprovado];
            });

        using var resposta = await client.PostAsync($"/api/pedidos/{pedidoId}/cobranca/conferir", null);

        resposta.StatusCode.Should().Be(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync());
        await mp.DidNotReceive().EstornarAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        using var verificacao = factory.Services.CreateScope();
        var banco = verificacao.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        banco.SetMobileTenantContext(EmpresaId);
        var relido = await banco.Pedidos.Include(p => p.Pagamentos).SingleAsync(p => p.Id == pedidoId);
        relido.Pagamentos.Should().ContainSingle("o pagamento entra uma vez só");
        relido.TotalPago.Should().Be(100m);
        var cobranca = await banco.CobrancasPedido.SingleAsync(p => p.PedidoId == pedidoId);
        cobranca.Status.Should().Be(StatusCobrancaPedido.Paga);
        cobranca.Motivo.Should().BeNull("nada de pagamento duplicado gravado");
    }
}

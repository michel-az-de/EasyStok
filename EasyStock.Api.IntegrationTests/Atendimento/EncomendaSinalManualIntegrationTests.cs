using System.Net;
using System.Net.Http.Json;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace EasyStock.Api.IntegrationTests.Atendimento;

public sealed partial class PedidoConversaConcorrenciaTests
{
    [SkippableFact]
    public async Task EncomendaSemMercadoPago_ComandaPersisteSinalManualCumulativoESaldoDaEntrega()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        var mp = Substitute.For<IMercadoPagoClient>();
        mp.CriarPreferenceAsync(Arg.Any<CriarPreferenceCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new MercadoPagoNaoConfiguradoException());
        await using var factory = CriarFactory(mp);
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory);
        var token = await Link(factory, c.ConversaId);
        var estado = await Estado(client, token);
        await Salvar(client, token, estado, estado.Rascunho with
        {
            Itens = [new(c.CardapioItemId, 1)], JanelaId = c.JanelaId, DataEntrega = c.DataEntrega,
        });
        estado = await Estado(client, token);
        var envio = await Enviar(client, token, estado.Revisao);
        (await Enviar(client, token, estado.Revisao)).PedidoId.Should().Be(envio.PedidoId);
        envio.Total.Should().Be(15m);
        var inicial = await Relido();
        inicial.Status.Should().Be("aguardando_pagamento");
        inicial.Pago.Should().Be(0m);
        inicial.Cobrancas.Should().Be(1);

        await Receber("pix", 7.49m);
        var parcial = await Relido();
        parcial.Status.Should().Be("aguardando_pagamento");
        parcial.Pago.Should().Be(7.49m);
        await Receber("dinheiro", 0.01m);
        var sinal = await Relido();
        sinal.Status.Should().Be("aguardando");
        sinal.Pago.Should().Be(7.5m);
        (await Estado(client, token)).Financeiro!.Alocado.Should().Be(7.5m);
        await Receber("credito", 7.5m);
        var quitado = await Relido();
        quitado.Pago.Should().Be(15m);
        quitado.Cobrancas.Should().Be(1);

        async Task Receber(string metodo, decimal valor)
        {
            var resposta = await client.PostAsJsonAsync($"/api/pedidos/{envio.PedidoId}/pagamentos",
                new { EmpresaId, Metodo = metodo, Valor = valor });
            resposta.StatusCode.Should().Be(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync());
        }

        async Task<(string Status, decimal Pago, int Cobrancas)> Relido()
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(EmpresaId);
            var pedido = await db.Pedidos.Include(p => p.Pagamentos)
                .SingleAsync(p => p.EmpresaId == EmpresaId && p.Id == envio.PedidoId);
            var cobranca = await db.CobrancasPedido.SingleAsync(p => p.EmpresaId == EmpresaId && p.PedidoId == envio.PedidoId);
            cobranca.EhOnline.Should().BeFalse();
            cobranca.ExpiraEm.Should().BeNull();
            cobranca.Valor.Should().Be(15m);
            return (pedido.Status, pedido.TotalPago, await db.CobrancasPedido.CountAsync(p => p.PedidoId == pedido.Id));
        }
    }
}

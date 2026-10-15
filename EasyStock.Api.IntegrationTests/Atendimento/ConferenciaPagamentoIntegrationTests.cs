using System.Net;
using System.Net.Http.Json;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Sales;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EasyStock.Api.IntegrationTests.Atendimento;

public sealed partial class PedidoConversaConcorrenciaTests
{
    [SkippableTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public async Task TrocarLinkParaManual_ConsultaPagamentoAprovadoAntesDeCancelarCobrarDeNovo(bool conferirPeloEndpoint, bool receberDireto)
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        var mp = Provedor(new());
        mp.ObterContaRecebedoraAsync(Arg.Any<CancellationToken>()).Returns("conta-1733");
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
            if (receberDireto) pedido.Status = StatusPedidoMapper.Aguardando;
            pedido.Itens.Add(new PedidoItem
            {
                Id = Guid.NewGuid(), PedidoId = pedido.Id, Nome = "Pedido de conferência",
                Quantidade = 1, PrecoUnitario = 100m, Subtotal = 100m, CriadoEm = DateTime.UtcNow,
            });
            pedido.RecalcularTotal();
            db.Pedidos.Add(pedido);
            pedidoId = pedido.Id;
            db.CobrancasPedido.Add(CobrancaPedido.CriarOnline(EmpresaId, pedidoId, 100m,
                "preferencia-1733", "https://pagamento-teste.invalid/1733", DateTime.UtcNow.AddMinutes(30),
                1, DateTime.UtcNow, contaRecebedoraId: "conta-1733"));
            await db.SaveChangesAsync();
        }
        mp.BuscarPagamentosPorReferenciaAsync(pedidoId.ToString(), Arg.Any<CancellationToken>())
            .Returns([new PagamentoMercadoPago("pagamento-1733", "approved", null, pedidoId.ToString(),
                100m, DateTime.UtcNow, "pix", "bank_transfer", CollectorId: "conta-1733")]);

        using var resposta = conferirPeloEndpoint
            ? await client.PostAsync($"/api/pedidos/{pedidoId}/cobranca/conferir", null)
            : receberDireto
            ? await client.PostAsJsonAsync($"/api/pedidos/{pedidoId}/pagamentos", new { EmpresaId, PedidoId = pedidoId, Metodo = "pix", Valor = 10m })
            : await client.PostAsJsonAsync($"/api/pedidos/{pedidoId}/cobranca/forma", new { forma = "na_entrega" });

        resposta.StatusCode.Should().Be(conferirPeloEndpoint ? HttpStatusCode.OK
            : receberDireto ? HttpStatusCode.BadRequest : HttpStatusCode.Conflict, await resposta.Content.ReadAsStringAsync());
        if (receberDireto)
        {
            (await resposta.Content.ReadAsStringAsync()).Should().Contain("Confira o pagamento");
            await mp.DidNotReceiveWithAnyArgs().BuscarPagamentosPorReferenciaAsync(default!, default);
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
                db.SetMobileTenantContext(EmpresaId);
                (await db.Pedidos.Include(p => p.Pagamentos).SingleAsync(p => p.Id == pedidoId)).TotalPago.Should().Be(0m);
                (await db.CobrancasPedido.SingleAsync(p => p.PedidoId == pedidoId)).EstaPendente.Should().BeTrue();
            }
            using var conferencia = await client.PostAsync($"/api/pedidos/{pedidoId}/cobranca/conferir", null);
            conferencia.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        if (conferirPeloEndpoint)
        {
            (await resposta.Content.ReadAsStringAsync()).Should().Contain("\"situacao\":\"confirmado\"");
            using var repetida = await client.PostAsync($"/api/pedidos/{pedidoId}/cobranca/conferir", null);
            repetida.StatusCode.Should().Be(HttpStatusCode.OK);
            (await repetida.Content.ReadAsStringAsync()).Should().Contain("\"situacao\":\"ja_conferido\"");
        }
        await mp.Received(1).BuscarPagamentosPorReferenciaAsync(pedidoId.ToString(), Arg.Any<CancellationToken>());
        await mp.DidNotReceive().ExpirarPreferenciaAsync(Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await mp.DidNotReceive().EstornarAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        using var verificacao = factory.Services.CreateScope();
        var banco = verificacao.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        banco.SetMobileTenantContext(EmpresaId);
        var relido = await banco.Pedidos.Include(p => p.Pagamentos).SingleAsync(p => p.Id == pedidoId);
        relido.TotalPago.Should().Be(100m);
        var cobranca = await banco.CobrancasPedido.SingleAsync(p => p.PedidoId == pedidoId);
        cobranca.EhOnline.Should().BeTrue();
        cobranca.EstaPendente.Should().BeFalse();
    }

    [SkippableFact]
    public Task BaixaDireta_ComLinkPendenteEmPedidoOperacional_ExigeConferenciaSemReceber() =>
        TrocarLinkParaManual_ConsultaPagamentoAprovadoAntesDeCancelarCobrarDeNovo(false, true);
}

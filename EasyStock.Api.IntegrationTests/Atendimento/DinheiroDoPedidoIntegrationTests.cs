using System.Net;
using System.Net.Http.Json;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Enums.Pagamentos;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EasyStock.Api.IntegrationTests.Atendimento;

/// <summary>#1964: dinheiro do pedido com PostgreSQL real (sinal por fora e pagamento a menor devolvível).</summary>
public sealed partial class PedidoConversaConcorrenciaTests
{
    /// <summary>
    /// #1717-2 (decisão do Felipe em 13/10): encomenda com o link do sinal aberto e nada pago no provedor. O caminho do
    /// console (forma "na entrega" e depois o recebimento) cancela o link, expira a preferência e o sinal recebido por
    /// fora coloca a encomenda na fila.
    /// </summary>
    [SkippableFact]
    public async Task Encomenda_SinalPagoPorFora_TrocaParaEntregaEBaixaOSinal()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        var mp = Provedor(new());
        mp.ObterContaRecebedoraAsync(Arg.Any<CancellationToken>()).Returns("conta-1964");
        await using var factory = CriarFactory(mp);
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory);
        var pedidoId = await SemearPedidoAsync(factory, c.ClienteId, 100m, "preferencia-sinal-1964", 50m,
            agendadoParaEm: DateTime.UtcNow.AddDays(3));
        mp.BuscarPagamentosPorReferenciaAsync(pedidoId.ToString(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PagamentoMercadoPago>)[]);

        using var troca = await client.PostAsJsonAsync($"/api/pedidos/{pedidoId}/cobranca/forma", new { forma = "na_entrega" });
        troca.StatusCode.Should().Be(HttpStatusCode.OK, await troca.Content.ReadAsStringAsync());
        using var recebido = await client.PostAsJsonAsync($"/api/pedidos/{pedidoId}/pagamentos",
            new { empresaId = EmpresaId, pedidoId, valor = 50m, metodo = "pix" });
        recebido.StatusCode.Should().Be(HttpStatusCode.OK, await recebido.Content.ReadAsStringAsync());

        await mp.Received(1).ExpirarPreferenciaAsync("preferencia-sinal-1964", Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        using var verificacao = factory.Services.CreateScope();
        var banco = verificacao.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        banco.SetMobileTenantContext(EmpresaId);
        var relido = await banco.Pedidos.Include(p => p.Pagamentos).SingleAsync(p => p.Id == pedidoId);
        relido.TotalPago.Should().Be(50m);
        relido.Status.Should().Be("aguardando", "o sinal recebido por fora coloca a encomenda na fila");
        (await banco.CobrancasPedido.SingleAsync(x => x.PedidoId == pedidoId && x.ReferenciaExterna == "preferencia-sinal-1964"))
            .Status.Should().Be(StatusCobrancaPedido.Cancelada);
    }

    /// <summary>
    /// #1724-7: pagamento aprovado abaixo do link ficava retido no Mercado Pago. Agora vira recebimento que o estorno
    /// online acha e devolve; o pedido não é confirmado e a notificação repetida não duplica.
    /// </summary>
    [SkippableFact]
    public async Task PagamentoAMenor_FicaRegistradoEODevolveOEstornoOnline()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        var mp = Provedor(new());
        await using var factory = CriarFactory(mp, NivelAcesso.Gerente);
        var c = await SemearAsync(factory);
        var pedidoId = await SemearPedidoAsync(factory, c.ClienteId, 100m, "preferencia-menor-1964", 100m);
        mp.ConsultarPagamentoAsync("pagamento-menor-1964", Arg.Any<CancellationToken>())
            .Returns(new PagamentoMercadoPago("pagamento-menor-1964", "approved", null, pedidoId.ToString(), 60m,
                DateTime.UtcNow, "pix", "bank_transfer"));
        mp.ListarEstornosAsync("pagamento-menor-1964", Arg.Any<CancellationToken>()).Returns((IReadOnlyList<EstornoMercadoPagoResult>)[]);
        mp.EstornarAsync("pagamento-menor-1964", 60m, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new EstornoMercadoPagoResult("19641964", 60m, "approved", "pagamento-menor-1964", DateTime.UtcNow));

        using (var webhook = factory.Services.CreateScope())
        {
            var confirmar = webhook.ServiceProvider.GetRequiredService<ConfirmarPagamentoPedidoUseCase>();
            var input = new ConfirmarPagamentoPedidoInput(pedidoId, "pagamento-menor-1964", "approved", 60m, "pix", "bank_transfer");
            (await confirmar.ExecuteAsync(input)).Situacao.Should().Be(SituacaoConfirmacaoPagamento.ValorMenor);
            (await confirmar.ExecuteAsync(input)).Situacao.Should().Be(SituacaoConfirmacaoPagamento.ValorMenor);
        }

        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetCurrentTenant(EmpresaId);
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        db.SetMobileTenantContext(EmpresaId);
        var pedido = await db.Pedidos.AsNoTracking().Include(p => p.Pagamentos).SingleAsync(p => p.Id == pedidoId);
        pedido.Status.Should().Be("aguardando_pagamento", "pagamento a menor não confirma o pedido");
        var pagamento = pedido.Pagamentos.Should().ContainSingle("a notificação repetida não duplica").Subject;
        pagamento.Valor.Should().Be(60m);

        var estornos = scope.ServiceProvider.GetRequiredService<IEstornosOnlineService>();
        var consulta = await estornos.ConsultarAsync(EmpresaId, pedidoId);
        consulta.Pagamentos.Should().ContainSingle().Which.Disponivel.Should().Be(60m, "o valor retido é devolvível");
        var devolucao = await estornos.SolicitarAsync(new SolicitarEstornoOnlineInput(EmpresaId, pedidoId, Guid.NewGuid(),
            pagamento.Id, 60m, "Pagamento a menor", UsuarioId, "Gerente", NivelAcesso.Gerente));
        devolucao.Situacao.Should().Be(PedidoEstornoOnline.Confirmado);
    }

    private static async Task<Guid> SemearPedidoAsync(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory,
        Guid clienteId, decimal total, string preferencia, decimal valorLink, DateTime? agendadoParaEm = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        db.SetMobileTenantContext(EmpresaId);
        var pedido = Pedido.Criar(EmpresaId, origem: "whatsapp");
        pedido.ClienteId = clienteId;
        pedido.AgendadoParaEm = agendadoParaEm;
        pedido.Itens.Add(new PedidoItem
        {
            Id = Guid.NewGuid(), PedidoId = pedido.Id, Nome = "Pedido do dinheiro",
            Quantidade = 1, PrecoUnitario = total, Subtotal = total, CriadoEm = DateTime.UtcNow,
        });
        pedido.RecalcularTotal();
        pedido.Status = "aguardando_pagamento";
        db.Pedidos.Add(pedido);
        db.CobrancasPedido.Add(CobrancaPedido.CriarOnline(EmpresaId, pedido.Id, valorLink, preferencia,
            "https://pagamento-teste.invalid/1964", DateTime.UtcNow.AddMinutes(30), 1, DateTime.UtcNow,
            contaRecebedoraId: "conta-1964"));
        await db.SaveChangesAsync();
        return pedido.Id;
    }
}

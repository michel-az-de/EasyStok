using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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

    /// <summary>
    /// #2262: o Extrato do Financeiro só lia contas e <c>movimentos_caixa</c>, e o recebimento de pedido mora em
    /// <c>pedido_pagamentos</c>. O sinal pago pelo Mercado Pago tem de chegar uma vez às linhas do Caixa que o Extrato
    /// lê, também com o webhook repetido. A devolução online confirmada sai em movimentos e não apaga a entrada.
    /// </summary>
    [SkippableFact]
    public async Task SinalPagoPeloMercadoPago_EntraUmaVezNoExtratoEADevolucaoSai()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        var mp = Provedor(new());
        await using var factory = CriarFactory(mp, NivelAcesso.Gerente);
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory);
        var pedidoId = await SemearPedidoAsync(factory, c.ClienteId, 38m, "preferencia-sinal-2262", 19m,
            agendadoParaEm: DateTime.UtcNow.AddDays(3));
        mp.ConsultarPagamentoAsync("pagamento-sinal-2262", Arg.Any<CancellationToken>())
            .Returns(new PagamentoMercadoPago("pagamento-sinal-2262", "approved", null, pedidoId.ToString(), 19m,
                DateTime.UtcNow, "pix", "bank_transfer"));
        mp.ListarEstornosAsync("pagamento-sinal-2262", Arg.Any<CancellationToken>()).Returns((IReadOnlyList<EstornoMercadoPagoResult>)[]);
        mp.EstornarAsync("pagamento-sinal-2262", 19m, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new EstornoMercadoPagoResult("22622262", 19m, "approved", "pagamento-sinal-2262", DateTime.UtcNow));

        using (var webhook = factory.Services.CreateScope())
        {
            var confirmar = webhook.ServiceProvider.GetRequiredService<ConfirmarPagamentoPedidoUseCase>();
            var input = new ConfirmarPagamentoPedidoInput(pedidoId, "pagamento-sinal-2262", "approved", 19m, "pix", "bank_transfer");
            (await confirmar.ExecuteAsync(input)).Situacao.Should().Be(SituacaoConfirmacaoPagamento.Confirmado);
            (await confirmar.ExecuteAsync(input)).Situacao.Should().Be(SituacaoConfirmacaoPagamento.JaConfirmado);
        }

        var hoje = EasyStock.Application.Common.HorarioBrasil.Hoje().ToString("yyyy-MM-dd");
        var periodo = $"desde={hoje}&ate={hoje}";
        var entradas = await LinhasDoCaixaAsync(client, periodo);
        var entrada = entradas.Should().ContainSingle("o webhook repetido não duplica a entrada").Subject;
        entrada.GetProperty("tipo").GetString().Should().Be("pagamento");
        entrada.GetProperty("origem").GetString().Should().Be("Pedido");
        entrada.GetProperty("metodo").GetString().Should().Be("pix");
        entrada.GetProperty("valor").GetDecimal().Should().Be(19m);

        Guid pagamentoId;
        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetCurrentTenant(EmpresaId);
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(EmpresaId);
            pagamentoId = (await db.Pedidos.AsNoTracking().Include(p => p.Pagamentos).SingleAsync(p => p.Id == pedidoId))
                .Pagamentos.Single().Id;
            var estornos = scope.ServiceProvider.GetRequiredService<IEstornosOnlineService>();
            (await estornos.SolicitarAsync(new SolicitarEstornoOnlineInput(EmpresaId, pedidoId, Guid.NewGuid(),
                pagamentoId, 19m, "Teste da devolução", UsuarioId, "Gerente", NivelAcesso.Gerente)))
                .Situacao.Should().Be(PedidoEstornoOnline.Confirmado);
        }

        using var movimentos = JsonDocument.Parse(await client.GetStringAsync($"/api/caixa/movimentos?{periodo}"));
        var saida = movimentos.RootElement.GetProperty("data").EnumerateArray().Should().ContainSingle().Subject;
        saida.GetProperty("tipo").GetString().Should().Be("saida");
        saida.GetProperty("valor").GetDecimal().Should().Be(19m);
        (await LinhasDoCaixaAsync(client, periodo)).Should().ContainSingle("a devolução sai à parte e a entrada fica no histórico")
            .Which.GetProperty("valor").GetDecimal().Should().Be(19m);
    }

    private static async Task<List<JsonElement>> LinhasDoCaixaAsync(HttpClient client, string periodo)
    {
        using var resposta = await client.GetAsync($"/api/caixa/linhas?{periodo}");
        resposta.StatusCode.Should().Be(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("data").EnumerateArray().Select(l => l.Clone()).ToList();
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

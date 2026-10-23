using System.Net;
using System.Net.Http.Json;
using EasyStock.Application.Common;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Services.Storefront;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Sales;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace EasyStock.Api.IntegrationTests.Atendimento;

public sealed partial class PedidoConversaConcorrenciaTests
{
    // #1800: falha temporária não é ausência de credencial. A encomenda sem credencial
    // conserva cobrança manual desde #1736, coberta em EncomendaSinalManualIntegrationTests.
    // #1961: a cobrança sai depois do commit do pedido; com o provedor fora, a reserva é desfeita (pedido cancelado com o
    // motivo, vaga liberada) e o link volta para o cliente reenviar. Nenhum pedido ATIVO fica para trás.
    [SkippableFact]
    public async Task Comanda_ProvedorOnlineIndisponivelPreservaSelecaoENaoDeixaPedido()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        var mp = Substitute.For<IMercadoPagoClient>();
        mp.CriarPreferenceAsync(Arg.Any<CriarPreferenceCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Provedor temporariamente indisponível"));
        await using var factory = CriarFactory(mp);
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory);
        var token = await Link(factory, c.ConversaId);
        var estado = await Estado(client, token);
        await Salvar(client, token, estado, estado.Rascunho with
        {
            Itens = [new(c.CardapioItemId, 1, null)], JanelaId = c.JanelaId, DataEntrega = c.DataEntrega,
        });

        var antes = await ContarPedidosAtivos(factory);
        var revisao = (await Estado(client, token)).Revisao;

        var resposta = await client.PostAsJsonAsync($"{Rota(token)}/confirmar", new { revisao });

        var corpo = await resposta.Content.ReadAsStringAsync();
        resposta.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, corpo);
        corpo.Should().NotContain("Erro interno");
        corpo.Should().Contain("tente de novo");
        (await ContarPedidosAtivos(factory)).Should().Be(antes, "pedido sem cobrança não fica ativo para trás");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(EmpresaId);
            var desfeito = await db.Pedidos.AsNoTracking().SingleAsync(p => p.ClienteId == c.ClienteId);
            desfeito.StatusEnum.Should().Be(StatusPedido.Cancelado);
            (await db.Set<PedidoEvento>().AsNoTracking().AnyAsync(e => e.PedidoId == desfeito.Id
                && e.Detalhes == CheckoutCoreService.MotivoMercadoPagoIndisponivel)).Should().BeTrue("o cancelamento diz o motivo");
            (await db.VagasOcupadas.AsNoTracking().AnyAsync(v => v.PedidoId == desfeito.Id && v.LiberadoEm == null))
                .Should().BeFalse("a vaga da janela volta");
            var link = await db.LinksCardapioConversa.AsNoTracking().SingleAsync(l => l.ConversaId == c.ConversaId);
            link.UsadoEm.Should().BeNull("o link volta para o cliente reenviar");
            link.ResultadoJson.Should().BeNull();
            link.PedidoRegistradoId.Should().BeNull();
        }
        (await Estado(client, token)).PodeEditar.Should().BeTrue("o cliente continua com a seleção para tentar de novo");

        mp.CriarPreferenceAsync(Arg.Any<CriarPreferenceCommand>(), Arg.Any<CancellationToken>())
            .Returns(x => new PreferenceCriadaResult(x.Arg<CriarPreferenceCommand>().IdempotencyKey!, "https://www.mercadopago.com.br/checkout/volta"));
        var novo = await Enviar(client, token, revisao);
        novo.LinkPagamento.Should().Be("https://www.mercadopago.com.br/checkout/volta", "com o provedor de volta, a mesma seleção vira pedido");
    }

    // #1716: sem credencial do Mercado Pago o cliente recebia "Erro interno do servidor" (500) ao confirmar.
    // #1811: vale para o pedido do dia. Entrega em outro dia é encomenda, e encomenda sem provedor aguarda o sinal
    // externo (#1736, EncomendaSinalManualIntegrationTests). O relógio da API fica no início do dia em Brasília, para a
    // janela das 9 h atender o prazo mínimo a qualquer hora em que o teste rode.
    // Decisão do Felipe (22/10, #1961): sem Mercado Pago configurado, o pedido do dia nasce para pagar na entrega, entra
    // na fila como o pedido na entrega e a equipe é avisada na conversa. Antes respondia 503 sem pedido.
    [SkippableFact]
    public async Task Comanda_PedidoDoDiaSemPagamentoOnlineConfiguradoNasceParaPagarNaEntregaEAvisaAEquipe()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        var mp = Substitute.For<IMercadoPagoClient>();
        mp.CriarPreferenceAsync(Arg.Any<CriarPreferenceCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new MercadoPagoNaoConfiguradoException());
        var hoje = HorarioBrasil.Hoje();
        var inicioDoDia = new DateTimeOffset(hoje.ToDateTime(new TimeOnly(0, 1)), TimeSpan.Zero)
            .AddMinutes(-HorarioBrasil.OffsetMinutosAtual());
        await using var factory = CriarFactory(mp).WithWebHostBuilder(b =>
            b.ConfigureTestServices(s => s.AddSingleton<TimeProvider>(new RelogioFixo(inicioDoDia))));
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory, dataEntrega: hoje);
        var token = await Link(factory, c.ConversaId);
        var estado = await Estado(client, token);
        await Salvar(client, token, estado, estado.Rascunho with
        {
            Itens = [new(c.CardapioItemId, 1, null)], JanelaId = c.JanelaId, DataEntrega = c.DataEntrega,
        });

        var resposta = await client.PostAsJsonAsync($"{Rota(token)}/confirmar", new { revisao = (await Estado(client, token)).Revisao });

        var corpo = await resposta.Content.ReadAsStringAsync();
        resposta.StatusCode.Should().Be(HttpStatusCode.OK, corpo);
        var envio = (await resposta.Content.ReadFromJsonAsync<Application.UseCases.Atendimento.Comanda.EnvioComandaCliente>())!;
        envio.LinkPagamento.Should().BeNull();
        envio.Saldo.Should().Be(15m);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        db.SetMobileTenantContext(EmpresaId);
        var pedido = await db.Pedidos.AsNoTracking().SingleAsync(p => p.Id == envio.PedidoId);
        pedido.StatusEnum.Should().Be(StatusPedido.Aguardando, "entra na fila como o pedido na entrega");
        var cobranca = await db.CobrancasPedido.AsNoTracking().SingleAsync(p => p.PedidoId == pedido.Id);
        cobranca.Provedor.Should().Be(CobrancaPedido.ProvedorNaEntrega);
        cobranca.Valor.Should().Be(15m);
        var textos = await db.AtendimentoMensagens.AsNoTracking()
            .Where(m => m.ConversaId == c.ConversaId && m.Autor == AutorMensagem.Sistema).Select(m => m.Texto!).ToListAsync();
        textos.Should().Contain(t => t.Contains("Pagamento online indisponível") && t.Contains("na entrega"), "a equipe é avisada na conversa");
        textos.Should().Contain(t => t.Contains("Recebemos seu pedido") && t.Contains("Pagamento na entrega"), "o cliente sabe que paga na entrega");
    }

    private sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => agora;
    }

    private static async Task<int> ContarPedidosAtivos(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<EasyStockDbContext>().Pedidos.IgnoreQueryFilters()
            .CountAsync(p => p.Status != "cancelado");
    }
}

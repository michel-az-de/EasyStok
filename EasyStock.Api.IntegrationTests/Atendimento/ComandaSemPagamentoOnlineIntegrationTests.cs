using System.Net;
using System.Net.Http.Json;
using EasyStock.Application.Common;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace EasyStock.Api.IntegrationTests.Atendimento;

public sealed partial class PedidoConversaConcorrenciaTests
{
    // #1716: sem credencial do Mercado Pago o cliente recebia "Erro interno do servidor" (500) ao confirmar.
    // #1811: vale para o pedido do dia. Entrega em outro dia é encomenda, e encomenda sem provedor aguarda o sinal
    // externo (#1736, EncomendaSinalManualIntegrationTests). O relógio da API fica no início do dia em Brasília, para a
    // janela das 9 h atender o prazo mínimo a qualquer hora em que o teste rode.
    [SkippableFact]
    public async Task Comanda_SemPagamentoOnlineConfiguradoRespondeIndisponivelENaoDeixaPedido()
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

        var antes = await ContarPedidos(factory);

        var resposta = await client.PostAsJsonAsync($"{Rota(token)}/confirmar", new { revisao = (await Estado(client, token)).Revisao });

        var corpo = await resposta.Content.ReadAsStringAsync();
        resposta.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, corpo);
        corpo.Should().NotContain("Erro interno");
        corpo.Should().Contain("combinar o pagamento");
        (await ContarPedidos(factory)).Should().Be(antes, "pedido sem cobrança não fica para trás");
        (await Estado(client, token)).PodeEditar.Should().BeTrue("o cliente continua com a seleção para tentar de novo");
    }

    private sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => agora;
    }

    private static async Task<int> ContarPedidos(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<EasyStockDbContext>().Pedidos.IgnoreQueryFilters().CountAsync();
    }
}

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
    // #1716: sem credencial do Mercado Pago o cliente recebia "Erro interno do servidor" (500) ao confirmar.
    [SkippableFact]
    public async Task Comanda_SemPagamentoOnlineConfiguradoRespondeIndisponivelENaoDeixaPedido()
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
            Itens = [new(c.CardapioItemId, 1, null)], JanelaId = c.JanelaId, DataEntrega = c.DataEntrega,
        });

        var resposta = await client.PostAsJsonAsync($"{Rota(token)}/confirmar", new { revisao = 1 });

        var corpo = await resposta.Content.ReadAsStringAsync();
        resposta.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, corpo);
        corpo.Should().NotContain("Erro interno");
        using var scope = factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<EasyStockDbContext>().Pedidos.IgnoreQueryFilters().CountAsync())
            .Should().Be(0, "pedido sem cobrança não fica para trás");
        (await Estado(client, token)).PodeEditar.Should().BeTrue("o cliente continua com a seleção para tentar de novo");
    }
}

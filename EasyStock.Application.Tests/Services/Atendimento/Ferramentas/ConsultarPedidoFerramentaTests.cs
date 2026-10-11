using System.Text.Json;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Application.Services.Atendimento.Ferramentas;
using EasyStock.Application.Services.Pedidos;
using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Application.Tests.Services.Atendimento.Ferramentas;

public sealed class ConsultarPedidoFerramentaTests
{
    [Fact]
    public async Task CupomEmitidoNaoConfundeDinheiroRecebidoComCoberturaDoPedido()
    {
        var empresa = Guid.NewGuid();
        var cliente = Cliente.Criar(empresa, "Maria");
        var pedido = Pedido.Criar(empresa, cliente);
        pedido.Total = EasyStock.Domain.ValueObjects.Dinheiro.FromDecimal(25m);
        pedido.Pagamentos.Add(new PedidoPagamento { Id = Guid.NewGuid(), PedidoId = pedido.Id, Metodo = "pix", Valor = 25 });
        var conversa = Conversa.Abrir(empresa, "5511999998888", DateTime.UtcNow, "Maria", cliente.Id);
        conversa.DefinirPedidoEmAndamento(pedido.Id);
        var pedidos = Substitute.For<IPedidoRepository>();
        pedidos.GetByIdWithDetailsAsync(empresa, pedido.Id).Returns(pedido);
        var creditos = Substitute.For<ICreditoClienteComandaRepository>();
        creditos.TotaisDoPedidoAsync(empresa, pedido.Id, Arg.Any<CancellationToken>()).Returns((10m, 0m));
        var ferramenta = new ConsultarPedidoFerramenta(pedidos, new(pedidos), Substitute.For<IConfiguracaoAtendimentoRepository>(),
            new SaldoFinanceiroPedido(pedidos, Substitute.For<IEstornoOnlineRepository>(), creditos), creditos);

        using var json = JsonDocument.Parse(await ferramenta.ExecutarAsync(new(empresa, conversa, DateTime.UtcNow), JsonSerializer.SerializeToElement(new { })));
        json.RootElement.GetProperty("pago").GetString().Should().Be("R$ 15,00");
        json.RootElement.GetProperty("recebido").GetString().Should().Be("R$ 25,00");
        json.RootElement.GetProperty("faltaPagar").GetString().Should().Be("R$ 10,00");
        json.RootElement.GetProperty("financeiro").GetProperty("convertidoEmCupom").GetDecimal().Should().Be(10);

        conversa.VincularCliente(Guid.NewGuid());
        (await ferramenta.ExecutarAsync(new(empresa, conversa, DateTime.UtcNow), JsonSerializer.SerializeToElement(new { })))
            .Should().Contain("pedido_nao_encontrado");
    }
}

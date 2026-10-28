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
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PedidoHistoricoDoMesmoClienteNaoRecebeNumeroDaComandaAtual(bool informarId)
    {
        var empresa = Guid.NewGuid(); var cliente = Cliente.Criar(empresa, "Maria");
        var pedido = Pedido.Criar(empresa, cliente);
        var conversa = Conversa.Abrir(empresa, "5511999998888", DateTime.UtcNow, "Maria", cliente.Id);
        typeof(Conversa).GetProperty(nameof(Conversa.NumeroComanda))!.SetValue(conversa, 7L);
        if (informarId) conversa.DefinirPedidoEmAndamento(Guid.NewGuid());
        var pedidos = Substitute.For<IPedidoRepository>();
        pedidos.GetByIdWithDetailsAsync(empresa, pedido.Id).Returns(pedido);
        pedidos.ListByClienteAsync(empresa, cliente.Id, 1).Returns([pedido]);
        var creditos = Substitute.For<ICreditoClienteComandaRepository>();
        var ferramenta = new ConsultarPedidoFerramenta(pedidos, new(pedidos), Substitute.For<IConfiguracaoAtendimentoRepository>(),
            new SaldoFinanceiroPedido(pedidos, Substitute.For<IEstornoOnlineRepository>(), creditos), creditos);
        var contexto = new ContextoTurnoAgente(empresa, conversa, DateTime.UtcNow);
        await ferramenta.ExecutarAsync(contexto, informarId ? JsonSerializer.SerializeToElement(new { pedido_id = pedido.Id }) : JsonSerializer.SerializeToElement(new { }));
        contexto.RespostaVerificada.Should().StartWith("Pedido registrado em").And.NotContain("Comanda #7");
    }

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

        var contexto = new ContextoTurnoAgente(empresa, conversa, DateTime.UtcNow);
        using var json = JsonDocument.Parse(await ferramenta.ExecutarAsync(contexto, JsonSerializer.SerializeToElement(new { })));
        json.RootElement.GetProperty("pago").GetString().Should().Be("R$ 15,00");
        json.RootElement.GetProperty("recebido").GetString().Should().Be("R$ 25,00");
        json.RootElement.GetProperty("faltaPagar").GetString().Should().Be("R$ 10,00");
        json.RootElement.GetProperty("financeiro").GetProperty("convertidoEmCupom").GetDecimal().Should().Be(10);
        contexto.RespostaVerificada.Should().Contain("Cobertura atual: R$ 15,00").And.Contain("Falta pagar: R$ 10,00")
            .And.Contain("Dinheiro recebido registrado: R$ 25,00").And.NotContain("crédito na sua conta")
            .And.Contain("Crédito emitido em cupom: R$ 10,00").And.NotContain("Devoluções registradas");

        conversa.VincularCliente(Guid.NewGuid());
        var outroCliente = new ContextoTurnoAgente(empresa, conversa, DateTime.UtcNow);
        (await ferramenta.ExecutarAsync(outroCliente, JsonSerializer.SerializeToElement(new { })))
            .Should().Contain("pedido_nao_encontrado");
        outroCliente.RespostaVerificada.Should().BeNull();
    }
}

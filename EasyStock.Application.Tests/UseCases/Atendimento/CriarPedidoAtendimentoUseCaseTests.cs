using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Storefront;
using EasyStock.Application.Tests.Services.Storefront;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Application.UseCases.CriarPedido;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Sales;

namespace EasyStock.Application.Tests.UseCases.Atendimento;

public class CriarPedidoAtendimentoUseCaseTests
{
    private static readonly DateTime Agora = new(2026, 6, 1, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task OrigemWhatsappEObservacaoPorItem()
    {
        var c = new CheckoutCoreServiceTests.Cenario();
        var empresaId = c.Storefront.EmpresaId;

        var cliente = new Cliente { Id = Guid.NewGuid(), EmpresaId = empresaId, Nome = "Maria" };
        var endereco = new ClienteEndereco { Id = Guid.NewGuid(), ClienteId = cliente.Id, Cep = "01310-100", Padrao = true };
        cliente.Enderecos.Add(endereco);
        var clienteRepo = Substitute.For<IClienteRepository>();
        clienteRepo.GetByIdWithDetailsAsync(empresaId, cliente.Id).Returns(cliente);

        var conversa = Conversa.Abrir(empresaId, "5511999998888", Agora, "Maria", cliente.Id);
        var conversaRepo = Substitute.For<IConversaRepository>();
        conversaRepo.ObterPorIdAsync(empresaId, conversa.Id, Arg.Any<CancellationToken>()).Returns(conversa);

        var unitOfWork = Substitute.For<IUnitOfWork>();

        var useCase = new CriarPedidoAtendimentoUseCase(c.Servico(), conversaRepo, clienteRepo, unitOfWork);

        var reservado = await useCase.ExecuteAsync(new CriarPedidoAtendimentoInput(
            EmpresaId: empresaId,
            ConversaId: conversa.Id,
            ClienteId: cliente.Id,
            Itens: new List<ItemPedidoCheckout> { new(c.CardapioItemId, 3, "sem granulado") },
            JanelaId: c.JanelaId,
            DataEntrega: c.DataEntrega,
            EnderecoId: endereco.Id,
            Observacoes: "tocar a campainha"));

        var pedido = reservado.Pedido;
        pedido.Origem.Should().Be(OrigemPedido.WhatsApp).And.Be("whatsapp");
        pedido.Status.Should().Be(StatusPedidoMapper.AguardandoPagamento);
        pedido.ClienteId.Should().Be(cliente.Id);
        pedido.Observacoes.Should().Be("tocar a campainha");

        var item = reservado.Itens.Should().ContainSingle().Subject;
        item.CardapioItemId.Should().Be(c.CardapioItemId);
        item.Nome.Should().Be("Brigadeiro");
        item.PrecoUnitario.Should().Be(10m);
        item.Observacao.Should().Be("sem granulado");

        await c.VagaRepo.Received(1).OcuparAsync(c.JanelaId, c.DataEntrega, pedido.Id, Arg.Any<CancellationToken>());
        conversa.PedidoEmAndamentoId.Should().Be(pedido.Id);
        await unitOfWork.Received(1).CommitAsync();
    }
}

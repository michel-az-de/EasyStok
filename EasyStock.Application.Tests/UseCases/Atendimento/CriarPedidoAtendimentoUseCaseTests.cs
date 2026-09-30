using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Storefront;
using EasyStock.Application.Tests.Services.Storefront;
using EasyStock.Application.Tests.UseCases.Pedidos.Cobranca;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Application.UseCases.CriarPedido;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
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

        var useCase = new CriarPedidoAtendimentoUseCase(c.Servico(), conversaRepo, clienteRepo, c.ConfiguracaoAtendimentoRepo, unitOfWork);

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

    [Fact]
    public async Task ConversaDeOutroCliente_RecusaSemCriarPedido()
    {
        var c = new CheckoutCoreServiceTests.Cenario();
        var empresaId = c.Storefront.EmpresaId;

        var cliente = new Cliente { Id = Guid.NewGuid(), EmpresaId = empresaId, Nome = "Maria" };
        var endereco = new ClienteEndereco { Id = Guid.NewGuid(), ClienteId = cliente.Id, Cep = "01310-100", Padrao = true };
        cliente.Enderecos.Add(endereco);
        var clienteRepo = Substitute.For<IClienteRepository>();
        clienteRepo.GetByIdWithDetailsAsync(empresaId, cliente.Id).Returns(cliente);

        var conversa = Conversa.Abrir(empresaId, "5511999998888", Agora, "Joana", Guid.NewGuid());
        var conversaRepo = Substitute.For<IConversaRepository>();
        conversaRepo.ObterPorIdAsync(empresaId, conversa.Id, Arg.Any<CancellationToken>()).Returns(conversa);

        var unitOfWork = Substitute.For<IUnitOfWork>();
        var useCase = new CriarPedidoAtendimentoUseCase(c.Servico(), conversaRepo, clienteRepo, c.ConfiguracaoAtendimentoRepo, unitOfWork);

        var act = () => useCase.ExecuteAsync(new CriarPedidoAtendimentoInput(
            EmpresaId: empresaId,
            ConversaId: conversa.Id,
            ClienteId: cliente.Id,
            Itens: new List<ItemPedidoCheckout> { new(c.CardapioItemId, 1) },
            JanelaId: c.JanelaId,
            DataEntrega: c.DataEntrega,
            EnderecoId: endereco.Id));

        await act.Should().ThrowAsync<RegraDeDominioVioladaException>();
        await c.VagaRepo.DidNotReceiveWithAnyArgs().OcuparAsync(default, default, default, default);
        conversa.PedidoEmAndamentoId.Should().BeNull();
        await unitOfWork.DidNotReceive().CommitAsync();
    }
    [Fact]
    public async Task JanelaAbaixoDoPrazoDaConfiguracao_Recusa()
    {
        // S16: janela 9-12h no dia da entrega; agora 08:00 em Brasília; padrão 60 + respiro 40 → 09:40.
        var c = new CheckoutCoreServiceTests.Cenario();
        c.Relogio.Advance(new DateTimeOffset(2026, 6, 2, 11, 0, 0, TimeSpan.Zero) - c.Relogio.GetUtcNow());
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
        var useCase = new CriarPedidoAtendimentoUseCase(c.Servico(), conversaRepo, clienteRepo, c.ConfiguracaoAtendimentoRepo, unitOfWork);

        var act = () => useCase.ExecuteAsync(new CriarPedidoAtendimentoInput(
            EmpresaId: empresaId,
            ConversaId: conversa.Id,
            ClienteId: cliente.Id,
            Itens: new List<ItemPedidoCheckout> { new(c.CardapioItemId, 1) },
            JanelaId: c.JanelaId,
            DataEntrega: c.DataEntrega,
            EnderecoId: endereco.Id));

        (await act.Should().ThrowAsync<RegraDeDominioVioladaException>()).WithMessage(CheckoutCoreService.JanelaAbaixoDoPrazo);
        conversa.PedidoEmAndamentoId.Should().BeNull();
        await unitOfWork.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task RecusaBloqueado()
    {
        var c = new CheckoutCoreServiceTests.Cenario();
        var empresaId = c.Storefront.EmpresaId;

        var cliente = new Cliente { Id = Guid.NewGuid(), EmpresaId = empresaId, Nome = "Maria" };
        var endereco = new ClienteEndereco { Id = Guid.NewGuid(), ClienteId = cliente.Id, Cep = "01310-100", Padrao = true };
        cliente.Enderecos.Add(endereco);
        cliente.Bloquear("não pagou", Agora);
        var clienteRepo = Substitute.For<IClienteRepository>();
        clienteRepo.GetByIdWithDetailsAsync(empresaId, cliente.Id).Returns(cliente);

        var conversa = Conversa.Abrir(empresaId, "5511999998888", Agora, "Maria", cliente.Id);
        var conversaRepo = Substitute.For<IConversaRepository>();
        conversaRepo.ObterPorIdAsync(empresaId, conversa.Id, Arg.Any<CancellationToken>()).Returns(conversa);

        var unitOfWork = Substitute.For<IUnitOfWork>();
        var useCase = new CriarPedidoAtendimentoUseCase(c.Servico(), conversaRepo, clienteRepo, c.ConfiguracaoAtendimentoRepo, unitOfWork);

        var act = () => useCase.ExecuteAsync(new CriarPedidoAtendimentoInput(
            EmpresaId: empresaId,
            ConversaId: conversa.Id,
            ClienteId: cliente.Id,
            Itens: new List<ItemPedidoCheckout> { new(c.CardapioItemId, 1) },
            JanelaId: c.JanelaId,
            DataEntrega: c.DataEntrega,
            EnderecoId: endereco.Id));

        (await act.Should().ThrowAsync<ClienteBloqueadoException>())
            .Which.Codigo.Should().Be("cliente_bloqueado");
        await c.VagaRepo.DidNotReceiveWithAnyArgs().OcuparAsync(default, default, default, default);
        conversa.PedidoEmAndamentoId.Should().BeNull();
        await unitOfWork.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task ForaDeAreaLiberado_PedidoRequerAprovacao()
    {
        var (reservado, _) = await CriarNaConversa(foraDeAreaLiberado: true);

        reservado.Pedido.RequerAprovacao.Should().BeTrue("a dona liberou o lead fora de área (S14)");
        reservado.Pedido.MotivoRequerAprovacao.Should().Be(CriarPedidoAtendimentoUseCase.MotivoForaDeArea);
    }

    [Fact]
    public async Task SemLiberacao_PedidoNaoRequerAprovacao()
    {
        var (reservado, _) = await CriarNaConversa(foraDeAreaLiberado: null);

        reservado.Pedido.RequerAprovacao.Should().BeFalse();
        reservado.Pedido.MotivoRequerAprovacao.Should().BeNull();
    }

    [Fact]
    public async Task ForaDeAreaLiberado_PagoVaiParaAprovacaoDaBaba()
    {
        var (reservado, _) = await CriarNaConversa(foraDeAreaLiberado: true);
        var f = new CobrancaPedidoFixture(pedido: reservado.Pedido);
        f.AdicionarOnline();

        var r = await f.Confirmar().ExecuteAsync(new ConfirmarPagamentoPedidoInput(
            reservado.Pedido.Id, PagamentoExternoId: "pay-1", StatusPagamento: "approved",
            ValorPago: reservado.Pedido.Total.Valor, MetodoPagamentoExterno: "pix",
            TipoPagamentoExterno: "bank_transfer", PagoEm: CobrancaPedidoFixture.Agora));

        r.Confirmado.Should().BeTrue();
        reservado.Pedido.Status.Should().Be(StatusPedidoMapper.AguardandoAprovacaoBaba,
            "pedido fora de área pago espera a dona, não vai direto para a cozinha");
    }

    private static async Task<(PedidoReservado Reservado, Conversa Conversa)> CriarNaConversa(bool? foraDeAreaLiberado)
    {
        var c = new CheckoutCoreServiceTests.Cenario();
        var empresaId = c.Storefront.EmpresaId;

        var cliente = new Cliente { Id = Guid.NewGuid(), EmpresaId = empresaId, Nome = "Maria" };
        var endereco = new ClienteEndereco { Id = Guid.NewGuid(), ClienteId = cliente.Id, Cep = "01310-100", Padrao = true };
        cliente.Enderecos.Add(endereco);
        var clienteRepo = Substitute.For<IClienteRepository>();
        clienteRepo.GetByIdWithDetailsAsync(empresaId, cliente.Id).Returns(cliente);

        var conversa = Conversa.Abrir(empresaId, "5511999998888", Agora, "Maria", cliente.Id);
        ContextoConversaJson.Gravar(conversa, ContextoConversaJson.ForaDeAreaLiberado, foraDeAreaLiberado);
        var conversaRepo = Substitute.For<IConversaRepository>();
        conversaRepo.ObterPorIdAsync(empresaId, conversa.Id, Arg.Any<CancellationToken>()).Returns(conversa);

        var useCase = new CriarPedidoAtendimentoUseCase(
            c.Servico(), conversaRepo, clienteRepo, c.ConfiguracaoAtendimentoRepo, Substitute.For<IUnitOfWork>());

        var reservado = await useCase.ExecuteAsync(new CriarPedidoAtendimentoInput(
            EmpresaId: empresaId,
            ConversaId: conversa.Id,
            ClienteId: cliente.Id,
            Itens: new List<ItemPedidoCheckout> { new(c.CardapioItemId, 2) },
            JanelaId: c.JanelaId,
            DataEntrega: c.DataEntrega,
            EnderecoId: endereco.Id));

        return (reservado, conversa);
    }
}

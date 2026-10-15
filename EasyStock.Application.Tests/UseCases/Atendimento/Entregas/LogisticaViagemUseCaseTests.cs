using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Entregas;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Sales;
using System.Text.Json;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Entregas;

public sealed class LogisticaViagemUseCaseTests
{
    [Theory]
    [InlineData("adicionar", "a_caminho")]
    [InlineData("remover", "a_caminho")]
    [InlineData("reordenar", "a_caminho")]
    [InlineData("adicionar", "cancelado")]
    [InlineData("remover", "devolvido")]
    [InlineData("remover", "entregue")]
    public async Task Rota_alterada_nao_atualiza_pedidos_nem_despacha(string alteracao, string evento)
    {
        var empresa = Guid.NewGuid(); var primeiro = Guid.NewGuid(); var segundo = Guid.NewGuid();
        var viagem = Viagem.Criar(empresa, DateTime.UtcNow);
        viagem.IncluirParada(primeiro, false); viagem.IncluirParada(segundo, false);
        if (alteracao == "adicionar") viagem.IncluirParada(Guid.NewGuid(), false);
        else if (alteracao == "remover") viagem.RetirarParada(primeiro);
        else viagem.ReordenarParada(segundo, 1);
        var viagens = Substitute.For<IViagemRepository>(); var pedidos = Substitute.For<IPedidoRepository>();
        viagens.ObterAsync(empresa, viagem.Id, Arg.Any<CancellationToken>()).Returns(viagem);
        var despacho = new DespachoLogistica { EmpresaId = empresa, ViagemId = viagem.Id, PedidoIdsJson = JsonSerializer.Serialize(new[] { primeiro, segundo }) };
        var sut = new LogisticaViagemUseCase(viagens, Substitute.For<IEntregadorRepository>(), pedidos, Substitute.For<IPublicadorEventoIntegracao>(), TimeProvider.System);
        await FluentActions.Invoking(() => sut.AplicarAsync(despacho, evento, CancellationToken.None))
            .Should().ThrowAsync<UseCaseValidationException>().WithMessage("*rota foi alterada*");
        viagem.Situacao.Should().Be(SituacaoViagem.Montando);
        await pedidos.DidNotReceiveWithAnyArgs().UpdateAsync(default!);
    }

    [Fact]
    public async Task Acoes_manuais_nao_editam_viagem_com_frete_contratado()
    {
        var empresa = Guid.NewGuid(); var viagem = Guid.NewGuid(); var pedido = Guid.NewGuid();
        var viagens = Substitute.For<IViagemRepository>(); var uow = Substitute.For<IUnitOfWork>();
        viagens.VinculadaLogisticaAsync(empresa, viagem, Arg.Any<CancellationToken>()).Returns(true);
        var incluir = new IncluirParadaViagemUseCase(viagens, Substitute.For<IPedidoRepository>(), Substitute.For<IClienteRepository>(), uow);
        var retirar = new RetirarParadaViagemUseCase(viagens, uow);
        var reordenar = new ReordenarParadaViagemUseCase(viagens, uow);
        var desfazer = new DesfazerViagemUseCase(viagens, uow);
        var entregador = new DefinirEntregadorViagemUseCase(viagens, Substitute.For<IEntregadorRepository>(), uow);
        foreach (var acao in new Func<Task>[] { () => incluir.ExecuteAsync(empresa, viagem, pedido), () => retirar.ExecuteAsync(empresa, viagem, pedido),
            () => reordenar.ExecuteAsync(empresa, viagem, pedido, 1), () => desfazer.ExecuteAsync(empresa, viagem), () => entregador.ExecuteAsync(empresa, viagem, null) })
            await FluentActions.Invoking(acao).Should().ThrowAsync<UseCaseValidationException>().WithMessage("*frete contratado*");
        await uow.DidNotReceive().CommitAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Retorno_reabre_so_pedido_que_nao_foi_entregue(bool jaEntregue)
    {
        var empresa = Guid.NewGuid();
        var pedido = Pedido.Criar(empresa); pedido.Status = "pronto"; pedido.MudarStatus(StatusPedido.SaiuParaEntrega);
        var viagem = Viagem.Criar(empresa, DateTime.UtcNow);
        var entregador = Entregador.Criar(empresa, "Demo", TipoEntregador.Proprio, EmpresaEntregador.Propria, null, null, null, DateTime.UtcNow);
        viagem.DefinirEntregador(entregador); viagem.IncluirParada(pedido.Id, false); viagem.Sair(entregador, DateTime.UtcNow);
        if (jaEntregue) { viagem.MarcarParadaEntregue(pedido.Id, DateTime.UtcNow); pedido.MarcarEntregue(); }
        var viagens = Substitute.For<IViagemRepository>(); var pedidos = Substitute.For<IPedidoRepository>();
        viagens.ObterAsync(empresa, viagem.Id, Arg.Any<CancellationToken>()).Returns(viagem);
        pedidos.GetByIdAsync(empresa, pedido.Id).Returns(pedido);
        var despacho = new DespachoLogistica { EmpresaId = empresa, ViagemId = viagem.Id, UsuarioId = Guid.NewGuid(), PedidoIdsJson = JsonSerializer.Serialize(new[] { pedido.Id }) };
        var sut = new LogisticaViagemUseCase(viagens, Substitute.For<IEntregadorRepository>(), pedidos, Substitute.For<IPublicadorEventoIntegracao>(), TimeProvider.System);

        await sut.AplicarAsync(despacho, "devolvido", CancellationToken.None);

        pedido.StatusEnum.Should().Be(jaEntregue ? StatusPedido.Entregue : StatusPedido.Pronto);
        viagem.Situacao.Should().Be(jaEntregue ? SituacaoViagem.Concluida : SituacaoViagem.Desfeita);
        if (jaEntregue)
        {
            viagem.Paradas.Should().ContainSingle();
            await pedidos.DidNotReceiveWithAnyArgs().UpdateAsync(default!);
        }
        else
        {
            viagem.Paradas.Should().BeEmpty();
            await pedidos.Received(1).UpdateAsync(pedido);
            await pedidos.Received(1).AddEventoAsync(Arg.Is<PedidoEvento>(e => e.StatusNovo == "pronto"));
        }
        PedidoStateMachine.DescontaEstoque(pedido.StatusEnum).Should().BeTrue();
    }
}

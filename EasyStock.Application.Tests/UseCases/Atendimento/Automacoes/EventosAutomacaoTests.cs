using EasyStock.Application.Tests.Helpers;
using EasyStock.Application.Events.Atendimento;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Integration;
using EasyStock.TestHelpers;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Automacoes;

/// <summary>S42: quem publica os eventos das automáticas e quem os ignora.</summary>
public class EventosAutomacaoTests
{
    private readonly Guid _empresaId = Guid.NewGuid();

    [Fact]
    public async Task EncerrarPublicaConversaEncerradaNoMesmoCommit()
    {
        var conversas = Substitute.For<IConversaRepository>().EspelharLeituraComLock();
        var publicador = Substitute.For<IPublicadorEventoIntegracao>();
        var conversa = Conversa.Abrir(_empresaId, "5511988887777", DateTime.UtcNow);
        conversas.ObterPorIdAsync(_empresaId, conversa.Id, Arg.Any<CancellationToken>()).Returns(conversa);
        conversas.ObterPorIdComLockAsync(_empresaId, conversa.Id, Arg.Any<CancellationToken>()).Returns(conversa);
        var uow = new FakeUnitOfWork();

        await new GerenciarConversaAtendimentoUseCase(conversas, uow, publicador)
            .EncerrarAsync(new AcaoConversaCommand(_empresaId, Guid.NewGuid(), conversa.Id));

        await publicador.Received(1).PublicarAsync(_empresaId, ConversaEncerradaEvent.TipoEvento, "Conversa", conversa.Id,
            Arg.Is<ConversaEncerradaEvent>(e => e.ConversaId == conversa.Id), Arg.Any<int>(), Arg.Any<string?>(),
            Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
        uow.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task EncerrarConversaJaEncerradaNaoPublicaDeNovo()
    {
        // #1292: Encerrar é idempotente; o evento também precisa ser, senão a automática de encerramento sai 2x.
        var conversas = Substitute.For<IConversaRepository>().EspelharLeituraComLock();
        var publicador = Substitute.For<IPublicadorEventoIntegracao>();
        var conversa = Conversa.Abrir(_empresaId, "5511988887777", DateTime.UtcNow);
        conversa.Encerrar(DateTime.UtcNow);
        conversas.ObterPorIdAsync(_empresaId, conversa.Id, Arg.Any<CancellationToken>()).Returns(conversa);
        conversas.ObterPorIdComLockAsync(_empresaId, conversa.Id, Arg.Any<CancellationToken>()).Returns(conversa);

        await new GerenciarConversaAtendimentoUseCase(conversas, new FakeUnitOfWork(), publicador)
            .EncerrarAsync(new AcaoConversaCommand(_empresaId, Guid.NewGuid(), conversa.Id));

        await publicador.DidNotReceive().PublicarAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<Guid>(), Arg.Any<ConversaEncerradaEvent>(), Arg.Any<int>(), Arg.Any<string?>(),
            Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// #1786: dois toques em Encerrar (ou duas atendentes) ao mesmo tempo. A leitura sem trava ainda via a conversa aberta;
    /// sob trava, a outra já encerrou e o evento (e a automática de encerramento) não sai de novo.
    /// </summary>
    [Fact]
    public async Task EncerramentoConcorrenteReleSobTravaENaoPublicaDeNovo()
    {
        var conversas = Substitute.For<IConversaRepository>();
        var publicador = Substitute.For<IPublicadorEventoIntegracao>();
        var aberta = Conversa.Abrir(_empresaId, "5511988887777", DateTime.UtcNow);
        conversas.ObterPorIdAsync(_empresaId, aberta.Id, Arg.Any<CancellationToken>()).Returns(aberta);
        var uow = new FakeUnitOfWork();
        var dentro = false;
        conversas.ObterPorIdComLockAsync(_empresaId, aberta.Id, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            dentro = uow.EmTransacao;
            // O que está gravado agora: a outra requisição já encerrou e confirmou.
            aberta.Encerrar(DateTime.UtcNow);
            return aberta;
        });

        var r = await new GerenciarConversaAtendimentoUseCase(conversas, uow, publicador)
            .EncerrarAsync(new AcaoConversaCommand(_empresaId, Guid.NewGuid(), aberta.Id));

        r.Situacao.Should().Be(EasyStock.Domain.Enums.Atendimento.SituacaoConversa.Encerrada);
        dentro.Should().BeTrue();
        await publicador.DidNotReceive().PublicarAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<Guid>(), Arg.Any<ConversaEncerradaEvent>(), Arg.Any<int>(), Arg.Any<string?>(),
            Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PedidoEntregueIgnoraOutrosStatus()
    {
        var pedidos = Substitute.For<IPedidoRepository>();
        var tenant = Substitute.For<ITenantContextAccessor>();
        // O disparo não é tocado: sem status "entregue" o handler nem lê o pedido.
        var handler = new AutomacaoPedidoEntregueHandler(tenant, null!, pedidos);
        var evento = OutboxEventoIntegracao.Criar(_empresaId, AutomacaoPedidoEntregueHandler.Tipo, "Pedido", Guid.NewGuid(),
            """{"pedidoId":"8f3c6f2e-1111-4a3b-9c1d-000000000001","statusNovo":"preparando"}""");

        await handler.HandleAsync(evento, CancellationToken.None);

        tenant.Received(1).SetCurrentTenant(_empresaId);
        await pedidos.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
    }
}

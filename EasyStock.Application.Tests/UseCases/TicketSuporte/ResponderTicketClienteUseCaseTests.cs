using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Helpdesk;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.TicketSuporte;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Tests.UseCases.TicketSuporte;

public class ResponderTicketClienteUseCaseTests
{
    private readonly IClienteTicketRepository _ticketRepo = Substitute.For<IClienteTicketRepository>();
    private readonly ISlaResolver _slaResolver = Substitute.For<ISlaResolver>();
    private readonly INotificadorService _notificador = Substitute.For<INotificadorService>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private static readonly Guid EmpresaId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid UsuarioId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    public ResponderTicketClienteUseCaseTests()
    {
        _currentUser.EmpresaId.Returns(EmpresaId);
        _currentUser.UsuarioId.Returns(UsuarioId);
    }

    private ResponderTicketClienteUseCase Sut() =>
        new(_ticketRepo, _slaResolver, _notificador, _uow, _currentUser);

    private static AdminTicket TicketAtivo(TicketStatus status = TicketStatus.EmAtendimento) => new()
    {
        Id = Guid.NewGuid(),
        EmpresaId = EmpresaId,
        CriadoPorId = UsuarioId,
        Titulo = "Pedido atrasado",
        Descricao = "desc",
        Status = status,
        Prioridade = TicketPrioridade.Normal,
        Categoria = TicketCategoria.Duvida,
        Mensagens = new List<AdminTicketMensagem>(),
        CriadoEm = DateTime.UtcNow.AddDays(-2),
        AlteradoEm = DateTime.UtcNow.AddDays(-1)
    };

    [Fact]
    public async Task Responder_em_ticket_em_atendimento_adiciona_mensagem_e_publica_evento_cliente()
    {
        var ticket = TicketAtivo();
        _ticketRepo.GetByIdAsync(EmpresaId, ticket.Id, UsuarioId).Returns(ticket);

        await Sut().ExecuteAsync(new ResponderTicketClienteCommand(ticket.Id, "Obrigado pela atualizacao"));

        ticket.Mensagens.Should().HaveCount(1);
        ticket.Mensagens.First().IsAdmin.Should().BeFalse();
        ticket.Status.Should().Be(TicketStatus.EmAtendimento);

        await _slaResolver.DidNotReceive().ResolverAsync(
            Arg.Any<Guid>(), Arg.Any<TicketPrioridade>(),
            Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
        await _ticketRepo.Received(1).UpdateAsync(ticket);
        await _ticketRepo.Received().AddHistoricoAsync(Arg.Is<TicketHistorico>(h =>
            h.Acao == TicketAcaoHistorico.Comentario));
        await _notificador.Received(1).EnfileirarEventoAsync(
            TipoEventoNotificacao.TicketRespondidoCliente,
            EmpresaId,
            Arg.Any<string>(),
            Arg.Any<Guid?>(),
            Arg.Any<CancellationToken>());
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task Responder_em_ticket_resolvido_reabre_e_recalcula_SLA_e_zera_flags_violado()
    {
        var ticket = TicketAtivo(TicketStatus.Resolvido);
        ticket.ResolvidoEm = DateTime.UtcNow.AddHours(-1);
        ticket.SlaRespostaViolado = true;
        ticket.SlaResolucaoViolado = true;
        ticket.UltimoAlerta50PctEm = DateTime.UtcNow.AddDays(-1);
        ticket.UltimoAlerta80PctEm = DateTime.UtcNow.AddDays(-1);
        ticket.PrimeiraRespostaEm = DateTime.UtcNow.AddDays(-1);

        var novoPrazoResp = DateTime.UtcNow.AddHours(2);
        var novoPrazoResol = DateTime.UtcNow.AddHours(8);
        _slaResolver.ResolverAsync(EmpresaId, TicketPrioridade.Normal, Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(new SlaResolvido(120, 480, novoPrazoResp, novoPrazoResol));
        _ticketRepo.GetByIdAsync(EmpresaId, ticket.Id, UsuarioId).Returns(ticket);

        await Sut().ExecuteAsync(new ResponderTicketClienteCommand(ticket.Id, "Voltou a ocorrer"));

        ticket.Status.Should().Be(TicketStatus.Aberto);
        ticket.ResolvidoEm.Should().BeNull();
        ticket.SlaRespostaViolado.Should().BeFalse();
        ticket.SlaResolucaoViolado.Should().BeFalse();
        ticket.UltimoAlerta50PctEm.Should().BeNull();
        ticket.UltimoAlerta80PctEm.Should().BeNull();
        ticket.PrimeiraRespostaEm.Should().BeNull();
        ticket.PrazoResposta.Should().Be(novoPrazoResp);
        ticket.PrazoResolucao.Should().Be(novoPrazoResol);

        await _slaResolver.Received(1).ResolverAsync(EmpresaId, TicketPrioridade.Normal,
            Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
        // Deve gravar dois historicos: comentario + status alterado.
        await _ticketRepo.Received(2).AddHistoricoAsync(Arg.Any<TicketHistorico>());
    }

    [Fact]
    public async Task Responder_em_ticket_fechado_tambem_reabre()
    {
        var ticket = TicketAtivo(TicketStatus.Fechado);
        _slaResolver.ResolverAsync(EmpresaId, TicketPrioridade.Normal, Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(new SlaResolvido(120, 480, DateTime.UtcNow.AddHours(2), DateTime.UtcNow.AddHours(8)));
        _ticketRepo.GetByIdAsync(EmpresaId, ticket.Id, UsuarioId).Returns(ticket);

        await Sut().ExecuteAsync(new ResponderTicketClienteCommand(ticket.Id, "Continua quebrado"));

        ticket.Status.Should().Be(TicketStatus.Aberto);
        await _slaResolver.Received(1).ResolverAsync(EmpresaId, TicketPrioridade.Normal,
            Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Responder_ticket_inexistente_para_cliente_lanca_KeyNotFound()
    {
        _ticketRepo.GetByIdAsync(EmpresaId, Arg.Any<Guid>(), UsuarioId).Returns((AdminTicket?)null);

        var act = () => Sut().ExecuteAsync(new ResponderTicketClienteCommand(Guid.NewGuid(), "ok"));

        await act.Should().ThrowAsync<KeyNotFoundException>();
        await _notificador.DidNotReceive().EnfileirarEventoAsync(
            Arg.Any<TipoEventoNotificacao>(), Arg.Any<Guid>(),
            Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Responder_resposta_vazia_lanca_validacao(string? resposta)
    {
        var act = () => Sut().ExecuteAsync(new ResponderTicketClienteCommand(Guid.NewGuid(), resposta!));
        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*Resposta*");
    }

    [Fact]
    public async Task Responder_resposta_excedente_lanca_validacao()
    {
        var act = () => Sut().ExecuteAsync(new ResponderTicketClienteCommand(
            Guid.NewGuid(), new string('a', 5001)));
        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*Resposta*");
    }
}

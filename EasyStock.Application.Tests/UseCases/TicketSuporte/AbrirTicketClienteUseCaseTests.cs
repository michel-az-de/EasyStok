using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Helpdesk;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.TicketSuporte;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Tests.UseCases.TicketSuporte;

/// <summary>
/// Cobertura do fluxo cliente de abertura de ticket via PWA.
/// Antes da correção: SLA não era calculado, evento <c>TicketCriado</c>
/// não era publicado e historico ficava vazio. Estes testes blindam
/// contra regressao.
/// </summary>
public class AbrirTicketClienteUseCaseTests
{
    private readonly IClienteTicketRepository _ticketRepo = Substitute.For<IClienteTicketRepository>();
    private readonly IFaturaRepository _faturaRepo = Substitute.For<IFaturaRepository>();
    private readonly IPedidoRepository _pedidoRepo = Substitute.For<IPedidoRepository>();
    private readonly ISlaResolver _slaResolver = Substitute.For<ISlaResolver>();
    private readonly INotificadorService _notificador = Substitute.For<INotificadorService>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private static readonly Guid EmpresaId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UsuarioId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public AbrirTicketClienteUseCaseTests()
    {
        _currentUser.EmpresaId.Returns(EmpresaId);
        _currentUser.UsuarioId.Returns(UsuarioId);

        _slaResolver.ResolverAsync(EmpresaId, Arg.Any<TicketPrioridade>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(new SlaResolvido(
                MinutosResposta: 120,
                MinutosResolucao: 480,
                PrazoResposta: DateTime.UtcNow.AddMinutes(120),
                PrazoResolucao: DateTime.UtcNow.AddMinutes(480)));
    }

    private AbrirTicketClienteUseCase Sut() =>
        new(_ticketRepo, _faturaRepo, _pedidoRepo, _slaResolver, _notificador, _uow, _currentUser);

    [Fact]
    public async Task Abrir_deve_aplicar_SLA_e_publicar_evento_TicketCriado_e_gravar_historico()
    {
        AdminTicket? capturado = null;
        await _ticketRepo.InsertAsync(Arg.Do<AdminTicket>(t => capturado = t));

        var result = await Sut().ExecuteAsync(new AbrirTicketClienteCommand(
            "Pedido nao chegou", "Pedido #123 atrasou 3 dias", TicketCategoria.Duvida));

        capturado.Should().NotBeNull();
        capturado!.EmpresaId.Should().Be(EmpresaId);
        capturado.CriadoPorId.Should().Be(UsuarioId);
        capturado.PrazoResposta.Should().NotBeNull();
        capturado.PrazoResolucao.Should().NotBeNull();
        capturado.Mensagens.Should().HaveCount(1);
        capturado.Mensagens.First().IsAdmin.Should().BeFalse();

        await _slaResolver.Received(1).ResolverAsync(EmpresaId, TicketPrioridade.Normal, Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
        await _ticketRepo.Received(1).AddHistoricoAsync(Arg.Is<TicketHistorico>(h =>
            h.Acao == TicketAcaoHistorico.Criado && h.AutorId == UsuarioId));
        await _notificador.Received(1).PublicarEventoAsync(
            TipoEventoNotificacao.TicketCriado,
            EmpresaId,
            Arg.Any<Guid?>(),
            Arg.Any<string>(),
            Arg.Any<IDictionary<string, object?>>(),
            Arg.Any<CancellationToken>());
        await _uow.Received(1).CommitAsync();
        result.TicketId.Should().Be(capturado.Id);
    }

    [Theory]
    [InlineData(null, "ok descricao")]
    [InlineData("", "ok descricao")]
    [InlineData("  ", "ok descricao")]
    public async Task Abrir_deve_validar_titulo_obrigatorio(string? titulo, string descricao)
    {
        var act = () => Sut().ExecuteAsync(new AbrirTicketClienteCommand(
            titulo!, descricao, TicketCategoria.Duvida));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*Título*");
        await _ticketRepo.DidNotReceive().InsertAsync(Arg.Any<AdminTicket>());
    }

    [Fact]
    public async Task Abrir_deve_validar_titulo_excedente()
    {
        var titulo = new string('a', 201);
        var act = () => Sut().ExecuteAsync(new AbrirTicketClienteCommand(
            titulo, "ok", TicketCategoria.Duvida));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*Título*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Abrir_deve_validar_descricao_obrigatoria(string? descricao)
    {
        var act = () => Sut().ExecuteAsync(new AbrirTicketClienteCommand(
            "ok titulo", descricao!, TicketCategoria.Duvida));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*Descrição*");
    }

    [Fact]
    public async Task Abrir_com_FaturaId_inexistente_para_empresa_deve_falhar()
    {
        var faturaId = Guid.NewGuid();
        _faturaRepo.GetByIdAsync(EmpresaId, faturaId, Arg.Any<CancellationToken>())
            .Returns((Fatura?)null);

        var act = () => Sut().ExecuteAsync(new AbrirTicketClienteCommand(
            "Cobranca duplicada", "Houve cobranca em duplicidade", TicketCategoria.Financeiro,
            FaturaId: faturaId));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*Fatura*");
        await _ticketRepo.DidNotReceive().InsertAsync(Arg.Any<AdminTicket>());
        await _notificador.DidNotReceive().PublicarEventoAsync(
            Arg.Any<TipoEventoNotificacao>(), Arg.Any<Guid>(), Arg.Any<Guid?>(),
            Arg.Any<string>(), Arg.Any<IDictionary<string, object?>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Abrir_com_PedidoId_inexistente_para_empresa_deve_falhar()
    {
        var pedidoId = Guid.NewGuid();
        _pedidoRepo.GetByIdAsync(EmpresaId, pedidoId).Returns((EasyStock.Domain.Entities.Pedido?)null);

        var act = () => Sut().ExecuteAsync(new AbrirTicketClienteCommand(
            "Pedido travado", "Esta no preparando ha 2 dias", TicketCategoria.Incidente,
            PedidoId: pedidoId));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*Pedido*");
        await _ticketRepo.DidNotReceive().InsertAsync(Arg.Any<AdminTicket>());
        await _pedidoRepo.DidNotReceive().AddEventoAsync(Arg.Any<PedidoEvento>());
    }

    [Fact]
    public async Task Abrir_com_PedidoId_valido_deve_vincular_ticket_e_registrar_PedidoEvento()
    {
        var pedido = EasyStock.Domain.Entities.Pedido.Criar(EmpresaId);
        _pedidoRepo.GetByIdAsync(EmpresaId, pedido.Id).Returns(pedido);

        AdminTicket? capturadoTicket = null;
        PedidoEvento? capturadoEvento = null;
        await _ticketRepo.InsertAsync(Arg.Do<AdminTicket>(t => capturadoTicket = t));
        await _pedidoRepo.AddEventoAsync(Arg.Do<PedidoEvento>(e => capturadoEvento = e));

        await Sut().ExecuteAsync(new AbrirTicketClienteCommand(
            "Pedido nao saiu da preparacao", "Cliente reclamando", TicketCategoria.Incidente,
            PedidoId: pedido.Id));

        capturadoTicket.Should().NotBeNull();
        capturadoTicket!.PedidoId.Should().Be(pedido.Id);
        capturadoEvento.Should().NotBeNull();
        capturadoEvento!.PedidoId.Should().Be(pedido.Id);
        capturadoEvento.Tipo.Should().Be("ticket_aberto");
        capturadoEvento.Detalhes.Should().Contain(capturadoTicket.Id.ToString());
    }

    [Fact]
    public async Task Abrir_nao_deve_chamar_SlaResolver_quando_validacao_falha()
    {
        var act = () => Sut().ExecuteAsync(new AbrirTicketClienteCommand("", "ok", TicketCategoria.Duvida));
        await act.Should().ThrowAsync<UseCaseValidationException>();

        await _slaResolver.DidNotReceive().ResolverAsync(
            Arg.Any<Guid>(), Arg.Any<TicketPrioridade>(),
            Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
    }
}

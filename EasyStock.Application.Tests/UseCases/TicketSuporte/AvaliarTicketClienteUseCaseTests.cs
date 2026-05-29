using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.TicketSuporte;

namespace EasyStock.Application.Tests.UseCases.TicketSuporte;

public class AvaliarTicketClienteUseCaseTests
{
    private readonly IClienteTicketRepository _ticketRepo = Substitute.For<IClienteTicketRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private static readonly Guid EmpresaId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid UsuarioId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    public AvaliarTicketClienteUseCaseTests()
    {
        _currentUser.EmpresaId.Returns(EmpresaId);
        _currentUser.UsuarioId.Returns(UsuarioId);
    }

    private AvaliarTicketClienteUseCase Sut() => new(_ticketRepo, _uow, _currentUser);

    private static AdminTicket TicketResolvido() => new()
    {
        Id = Guid.NewGuid(), EmpresaId = EmpresaId, CriadoPorId = UsuarioId,
        Titulo = "x", Descricao = "y", Status = TicketStatus.Resolvido,
        Prioridade = TicketPrioridade.Normal, Categoria = TicketCategoria.Duvida,
        CriadoEm = DateTime.UtcNow.AddDays(-3),
        AlteradoEm = DateTime.UtcNow.AddHours(-1),
        ResolvidoEm = DateTime.UtcNow.AddHours(-1)
    };

    [Fact]
    public async Task Avaliar_ticket_resolvido_persiste_nota_e_comentario()
    {
        var t = TicketResolvido();
        _ticketRepo.GetByIdAsync(EmpresaId, t.Id, UsuarioId).Returns(t);

        await Sut().ExecuteAsync(new AvaliarTicketClienteCommand(t.Id, 5, "Atendimento excelente"));

        t.NotaCsat.Should().Be(5);
        t.ComentarioCsat.Should().Be("Atendimento excelente");
        t.AvaliadoEm.Should().NotBeNull();

        await _ticketRepo.Received(1).UpdateAsync(t);
        await _ticketRepo.Received(1).AddHistoricoAsync(Arg.Any<TicketHistorico>());
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task Avaliar_ticket_fechado_e_aceito()
    {
        var t = TicketResolvido();
        t.Status = TicketStatus.Fechado;
        _ticketRepo.GetByIdAsync(EmpresaId, t.Id, UsuarioId).Returns(t);

        await Sut().ExecuteAsync(new AvaliarTicketClienteCommand(t.Id, 4, null));
        t.NotaCsat.Should().Be(4);
        t.ComentarioCsat.Should().BeNull();
    }

    [Theory]
    [InlineData(TicketStatus.Aberto)]
    [InlineData(TicketStatus.EmAtendimento)]
    [InlineData(TicketStatus.AguardandoCliente)]
    public async Task Avaliar_ticket_nao_resolvido_lanca_validacao(TicketStatus status)
    {
        var t = TicketResolvido();
        t.Status = status;
        _ticketRepo.GetByIdAsync(EmpresaId, t.Id, UsuarioId).Returns(t);

        var act = () => Sut().ExecuteAsync(new AvaliarTicketClienteCommand(t.Id, 5, null));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*Resolvidos*");
        await _ticketRepo.DidNotReceive().UpdateAsync(Arg.Any<AdminTicket>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(6)]
    [InlineData(100)]
    public async Task Avaliar_nota_fora_da_faixa_1_a_5_lanca_validacao(int nota)
    {
        var act = () => Sut().ExecuteAsync(new AvaliarTicketClienteCommand(Guid.NewGuid(), nota, null));
        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*1*5*");
    }

    [Fact]
    public async Task Avaliar_comentario_excedente_lanca_validacao()
    {
        var act = () => Sut().ExecuteAsync(new AvaliarTicketClienteCommand(
            Guid.NewGuid(), 4, new string('a', 501)));
        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*500*");
    }

    [Fact]
    public async Task Avaliar_ticket_de_outro_cliente_retorna_KeyNotFound()
    {
        _ticketRepo.GetByIdAsync(EmpresaId, Arg.Any<Guid>(), UsuarioId).Returns((AdminTicket?)null);

        var act = () => Sut().ExecuteAsync(new AvaliarTicketClienteCommand(Guid.NewGuid(), 5, null));
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Avaliar_idempotente_substitui_nota_anterior()
    {
        var t = TicketResolvido();
        t.NotaCsat = 3;
        t.ComentarioCsat = "Mediano";
        t.AvaliadoEm = DateTime.UtcNow.AddDays(-1);
        _ticketRepo.GetByIdAsync(EmpresaId, t.Id, UsuarioId).Returns(t);

        await Sut().ExecuteAsync(new AvaliarTicketClienteCommand(t.Id, 5, "Reconsiderei"));

        t.NotaCsat.Should().Be(5);
        t.ComentarioCsat.Should().Be("Reconsiderei");
    }

    [Fact]
    public async Task Avaliar_comentario_em_branco_persiste_como_null()
    {
        var t = TicketResolvido();
        _ticketRepo.GetByIdAsync(EmpresaId, t.Id, UsuarioId).Returns(t);

        await Sut().ExecuteAsync(new AvaliarTicketClienteCommand(t.Id, 5, "   "));

        t.ComentarioCsat.Should().BeNull();
    }
}

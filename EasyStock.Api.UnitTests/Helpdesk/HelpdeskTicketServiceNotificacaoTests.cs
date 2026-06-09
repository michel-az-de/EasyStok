using EasyStock.Api.Services.Helpdesk;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Helpdesk;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Domain.Exceptions;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Helpdesk;

/// <summary>
/// ADR-0030 P0-1b: blinda o outbox transacional (EnfileirarEventoAsync ANTES do commit) e os
/// guards minimos de estado do HelpdeskTicketService. EF in-memory (sem interceptors/RLS —
/// suficiente para a logica do servico).
/// </summary>
public class HelpdeskTicketServiceNotificacaoTests : IDisposable
{
    private readonly EasyStockDbContext _db;
    private readonly ICurrentUserAccessor _user = Substitute.For<ICurrentUserAccessor>();
    private readonly ISlaResolver _sla = Substitute.For<ISlaResolver>();
    private readonly INotificadorService _notificador = Substitute.For<INotificadorService>();

    private static readonly Guid OperadorId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid EmpresaId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid CriadoPorId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    public HelpdeskTicketServiceNotificacaoTests()
    {
        _db = new EasyStockDbContext(new DbContextOptionsBuilder<EasyStockDbContext>()
            .UseInMemoryDatabase($"helpdesk-tickets-{Guid.NewGuid()}").Options);
        // O HasQueryFilter global usa CurrentTenantId; sem _currentUser cairia em Guid.Empty e
        // filtraria o ticket semeado. Fixa o tenant de teste para o filtro casar.
        _db.SetMobileTenantContext(EmpresaId);
        _user.UsuarioId.Returns(OperadorId);
        _user.TemPermissao(Arg.Any<Permissao>()).Returns(true);
        _sla.ResolverAsync(Arg.Any<Guid>(), Arg.Any<TicketPrioridade>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(new SlaResolvido(120, 480, DateTime.UtcNow.AddMinutes(120), DateTime.UtcNow.AddMinutes(480)));
    }

    private HelpdeskTicketService Sut() => new(_db, _user, _sla, _notificador);

    private AdminTicket SeedTicket(TicketStatus status)
    {
        var t = new AdminTicket
        {
            Id = Guid.NewGuid(),
            EmpresaId = EmpresaId,
            CriadoPorId = CriadoPorId,
            Titulo = "Teste",
            Descricao = "desc",
            Status = status,
            Prioridade = TicketPrioridade.Normal,
            Categoria = TicketCategoria.Duvida,
            Nivel = NivelAtendimento.N1,
            Mensagens = new List<AdminTicketMensagem>(),
            CriadoEm = DateTime.UtcNow.AddDays(-1),
            AlteradoEm = DateTime.UtcNow.AddDays(-1),
        };
        _db.AdminTickets.Add(t);
        _db.SaveChanges();
        return t;
    }

    [Theory]
    [InlineData(TicketStatus.Resolvido)]
    [InlineData(TicketStatus.Fechado)]
    public async Task Assumir_em_ticket_finalizado_lanca_RegraDeDominio(TicketStatus status)
    {
        var t = SeedTicket(status);
        var act = () => Sut().AssumirAsync(new AssumirTicketCommand(t.Id));
        await act.Should().ThrowAsync<RegraDeDominioVioladaException>();
    }

    [Fact]
    public async Task Encaminhar_em_ticket_fechado_lanca_RegraDeDominio()
    {
        var t = SeedTicket(TicketStatus.Fechado);
        var act = () => Sut().EncaminharAsync(new EncaminharNivelCommand(t.Id, NivelAtendimento.N2, null));
        await act.Should().ThrowAsync<RegraDeDominioVioladaException>();
    }

    [Fact]
    public async Task Responder_em_ticket_fechado_lanca_RegraDeDominio()
    {
        var t = SeedTicket(TicketStatus.Fechado);
        var act = () => Sut().ResponderAsync(new ResponderAdminTicketCommand(t.Id, "ola", false, null));
        await act.Should().ThrowAsync<RegraDeDominioVioladaException>();
    }

    [Fact]
    public async Task AlterarStatus_enfileira_TicketStatusAlterado_e_persiste()
    {
        var t = SeedTicket(TicketStatus.Aberto);
        await Sut().AlterarStatusAsync(new AlterarStatusTicketCommand(t.Id, TicketStatus.AguardandoCliente));

        await _notificador.Received(1).EnfileirarEventoAsync(
            TipoEventoNotificacao.TicketStatusAlterado, EmpresaId,
            Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
        (await _db.AdminTickets.FindAsync(t.Id))!.Status.Should().Be(TicketStatus.AguardandoCliente);
    }

    [Fact]
    public async Task Assumir_valido_atribui_e_nao_enfileira_auto_notificacao()
    {
        var t = SeedTicket(TicketStatus.Aberto);
        await Sut().AssumirAsync(new AssumirTicketCommand(t.Id));

        (await _db.AdminTickets.FindAsync(t.Id))!.AtendenteId.Should().Be(OperadorId);
        await _notificador.DidNotReceive().EnfileirarEventoAsync(
            Arg.Any<TipoEventoNotificacao>(), Arg.Any<Guid>(),
            Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    public void Dispose() => _db.Dispose();
}

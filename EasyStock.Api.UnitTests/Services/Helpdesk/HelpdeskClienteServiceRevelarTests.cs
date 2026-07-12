using EasyStock.Api.Services;
using EasyStock.Api.Services.Helpdesk;
using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Api.UnitTests.Services.Helpdesk;

/// <summary>
/// ADM-BOLA-1 (#918): a revelacao de PII deve validar que o ticket de contexto pertence a empresa
/// cujo dado sera desmascarado. Sem essa amarracao, um SuperAdmin no ticket do Tenant A revelava a
/// PII do Tenant B (cross-tenant) e gravava o historico no ticket errado. Ticket permanece opcional:
/// valida-se somente quando fornecido (decisao Felipe).
/// </summary>
public class HelpdeskClienteServiceRevelarTests : IDisposable
{
    private const string MotivoValido = "auditoria de suporte";
    private const string DocumentoB = "48735219000165";

    private readonly EasyStockDbContext _db;
    private readonly HelpdeskClienteService _svc;

    private readonly Guid _empresaA = Guid.NewGuid();
    private readonly Guid _empresaB = Guid.NewGuid();
    private readonly Guid _ticketA = Guid.NewGuid();
    private readonly Guid _ticketB = Guid.NewGuid();

    public HelpdeskClienteServiceRevelarTests()
    {
        var currentUser = new FakeSuperAdmin();
        _db = new EasyStockDbContext(
            new DbContextOptionsBuilder<EasyStockDbContext>()
                .UseInMemoryDatabase($"revelar-bola-tests-{Guid.NewGuid()}")
                .Options,
            currentUser);

        _db.Empresas.Add(new Empresa { Id = _empresaB, Nome = "Tenant B", Documento = DocumentoB });
        _db.AdminTickets.Add(new AdminTicket { Id = _ticketA, EmpresaId = _empresaA, Titulo = "T-A", Descricao = "d" });
        _db.AdminTickets.Add(new AdminTicket { Id = _ticketB, EmpresaId = _empresaB, Titulo = "T-B", Descricao = "d" });
        _db.SaveChanges();

        var audit = new AdminAuditService(_db, new HttpContextAccessor(), NullLogger<AdminAuditService>.Instance);
        _svc = new HelpdeskClienteService(_db, currentUser, audit);
    }

    [Fact]
    public async Task Revela_com_ticket_do_mesmo_tenant_retorna_desmascarado()
    {
        var res = await _svc.RevelarAsync(new RevelarClienteCommand(_empresaB, MotivoValido, _ticketB));

        res.Mascarado.Should().BeFalse();
        res.DocumentoExibicao.Should().Be(DocumentoB);
    }

    [Fact]
    public async Task Revela_sem_ticket_e_permitido()
    {
        var res = await _svc.RevelarAsync(new RevelarClienteCommand(_empresaB, MotivoValido, null));

        res.Mascarado.Should().BeFalse();
        res.DocumentoExibicao.Should().Be(DocumentoB);
    }

    [Fact]
    public async Task Revela_cross_tenant_lanca_unauthorized_e_nao_vaza_pii()
    {
        // ticket do Tenant A, revelando a empresa do Tenant B -> deve bloquear (403).
        var act = () => _svc.RevelarAsync(new RevelarClienteCommand(_empresaB, MotivoValido, _ticketA));

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        _db.TicketHistoricos.Should().BeEmpty("o reveal cross-tenant nao pode gravar historico");
    }

    [Fact]
    public async Task Revela_com_ticket_inexistente_lanca_keynotfound()
    {
        var act = () => _svc.RevelarAsync(new RevelarClienteCommand(_empresaB, MotivoValido, Guid.NewGuid()));

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    public void Dispose() => _db.Dispose();

    private sealed class FakeSuperAdmin : ICurrentUserAccessor
    {
        public Guid EmpresaId { get; } = Guid.NewGuid();
        public bool IsAuthenticated => true;
        public Guid UsuarioId { get; } = Guid.NewGuid();
        public NivelAcesso Nivel => NivelAcesso.SuperAdmin;
        public bool TemPermissao(Permissao permissao) => true;
    }
}

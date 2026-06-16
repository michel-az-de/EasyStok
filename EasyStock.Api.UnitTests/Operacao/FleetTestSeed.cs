using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Operacao;

/// <summary>
/// Builders de entidades para os testes do Centro de Comando da Frota (issue 623).
/// Mantem o seeding legivel e fora dos asserts. Nao cobre Fatura (mapeamento owned/json
/// e exercitado no teste Postgres Testcontainers, nao no InMemory).
/// </summary>
internal static class FleetTestSeed
{
    /// <summary>
    /// DbContext InMemory com contexto SuperAdmin — abre o HasQueryFilter global de
    /// tenant (e => IsSuperAdmin || EmpresaId == CurrentTenantId), do mesmo jeito que o
    /// endpoint admin roda em producao. Sem isso, AssinaturaEmpresa/Ticket/Fatura sao
    /// filtrados para EmpresaId vazio e a frota volta vazia.
    /// </summary>
    public static EasyStockDbContext SuperAdminDb(string nome)
    {
        var user = Substitute.For<ICurrentUserAccessor>();
        user.IsAuthenticated.Returns(true);
        user.Nivel.Returns(NivelAcesso.SuperAdmin);
        return new EasyStockDbContext(
            new DbContextOptionsBuilder<EasyStockDbContext>().UseInMemoryDatabase(nome).Options,
            user);
    }

    public static Empresa Empresa(Guid id, string nome)
    {
        var now = DateTime.UtcNow;
        return new Empresa
        {
            Id = id,
            Nome = nome,
            Documento = id.ToString("N")[..14],
            CriadoEm = now,
            AlteradoEm = now,
        };
    }

    public static Plano Plano(decimal precoMensal, string nome = "Plus") => new()
    {
        Id = Guid.NewGuid(),
        Nome = nome,
        PrecoMensal = precoMensal,
        Ativo = true,
        CriadoEm = DateTime.UtcNow,
    };

    public static AssinaturaEmpresa Assinatura(Guid empresaId, Guid planoId, StatusAssinatura status, DateTime? trialFim = null)
    {
        var now = DateTime.UtcNow;
        return new AssinaturaEmpresa
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            PlanoId = planoId,
            Status = status,
            DataInicio = now.AddDays(-30),
            CriadoEm = now.AddDays(-30),
            AlteradoEm = now,
            TrialFim = trialFim,
        };
    }

    public static Order Pedido(Guid empresaId, string status, decimal total, DateTime updatedAt,
        DateTime? createdAt = null, DateTime? confirmedAt = null) => new()
        {
            Id = Guid.NewGuid().ToString("N"),
            ClientSnapshotName = "Cliente",
            Status = status,
            Total = total,
            EmpresaId = empresaId,
            LojaId = Guid.NewGuid(),
            CreatedAt = createdAt ?? updatedAt,
            UpdatedAt = updatedAt,
            ConfirmedAt = confirmedAt,
        };

    public static MobileDevice Device(Guid empresaId, DateTime? lastSeenAt, bool revoked = false)
    {
        var now = DateTime.UtcNow;
        return new MobileDevice
        {
            Id = Guid.NewGuid().ToString("N"),
            ApiKeyHash = "hash-" + Guid.NewGuid().ToString("N")[..8],
            EmpresaId = empresaId,
            LojaId = Guid.NewGuid(),
            LastSeenAt = lastSeenAt,
            Revoked = revoked,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public static AdminTicket Ticket(Guid empresaId, bool slaViolado, TicketStatus status = TicketStatus.Aberto)
    {
        var now = DateTime.UtcNow;
        return new AdminTicket
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Titulo = "Ticket de teste",
            Descricao = "desc",
            Status = status,
            Prioridade = TicketPrioridade.Alta,
            Categoria = TicketCategoria.Duvida,
            Nivel = NivelAtendimento.N1,
            Mensagens = new List<AdminTicketMensagem>(),
            SlaRespostaViolado = slaViolado,
            CriadoEm = now,
            AlteradoEm = now,
        };
    }
}

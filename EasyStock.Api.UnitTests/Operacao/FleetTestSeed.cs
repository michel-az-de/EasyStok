using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Operacao;

/// <summary>
/// Builders das entidades dos testes da tela Operação (issue 623). Fonte = ERP (Vendas)
/// + conta (assinatura/plano/ticket). Faturas ficam no teste Postgres (owned/json não roda
/// bem no InMemory).
/// </summary>
internal static class FleetTestSeed
{
    /// <summary>
    /// DbContext InMemory com contexto SuperAdmin — abre o HasQueryFilter global de tenant
    /// (IsSuperAdmin || EmpresaId==tenant), do mesmo jeito que o endpoint admin roda em prod.
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
        return new Empresa { Id = id, Nome = nome, Documento = id.ToString("N")[..14], CriadoEm = now, AlteradoEm = now };
    }

    public static Plano Plano(decimal precoMensal, string nome = "Plus") =>
        new() { Id = Guid.NewGuid(), Nome = nome, PrecoMensal = precoMensal, Ativo = true, CriadoEm = DateTime.UtcNow };

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

    public static Venda Venda(Guid empresaId, decimal valor, DateTime dataVenda) =>
        new()
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ValorTotal = Dinheiro.FromDecimal(valor),
            DataVenda = dataVenda,
            CriadoEm = dataVenda,
        };

    public static AdminTicket Ticket(Guid empresaId, bool slaViolado, TicketStatus status = TicketStatus.Aberto)
    {
        var now = DateTime.UtcNow;
        return new AdminTicket
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Titulo = "Chamado de teste",
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

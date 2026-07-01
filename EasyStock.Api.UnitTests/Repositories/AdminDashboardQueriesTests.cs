using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Repositories;

/// <summary>
/// ADM-005 (#695): "usuários ativos" do dashboard contava TODOS os ativos
/// (db.Usuarios.Count(u => u.Ativo)), incluindo SuperAdmin/OPS internos sem vínculo a tenant,
/// divergindo da soma por-tenant. Agora conta só usuários ativos com vínculo UsuarioEmpresa.
/// </summary>
public class AdminDashboardQueriesTests : IDisposable
{
    private readonly EasyStockDbContext _db;
    private readonly IAssinaturaEmpresaRepository _assinaturaRepo;

    public AdminDashboardQueriesTests()
    {
        // O dashboard roda como SuperAdmin — contexto que bypassa o global query filter de
        // tenant (senão o vínculo UsuarioEmpresa ficaria invisível e Empresas.Any() daria 0).
        var superAdmin = Substitute.For<ICurrentUserAccessor>();
        superAdmin.IsAuthenticated.Returns(true);
        superAdmin.Nivel.Returns(NivelAcesso.SuperAdmin);

        _db = new EasyStockDbContext(
            new DbContextOptionsBuilder<EasyStockDbContext>()
                .UseInMemoryDatabase($"admin-dashboard-tests-{Guid.NewGuid()}")
                .Options,
            superAdmin);

        // MRR delega ao predicado canônico (fonte única); mock aqui, prova de runtime
        // (bypass RLS de verdade) fica no AdminDashboardQueriesPostgresTests.
        _assinaturaRepo = Substitute.For<IAssinaturaEmpresaRepository>();
    }

    [Fact]
    public async Task TotalUsuariosAtivos_conta_apenas_ativos_vinculados_a_tenant()
    {
        var empresaId = Guid.NewGuid();

        // Ativo + vinculado a tenant -> conta.
        _db.Usuarios.Add(ComVinculo("tenant-ativo@x.com", ativo: true, empresaId));
        // SuperAdmin/OPS: ativo, sem vínculo -> NÃO conta (era o que inflava o número).
        _db.Usuarios.Add(SemVinculo("superadmin@ops.com", ativo: true));
        // Vinculado mas inativo -> NÃO conta.
        _db.Usuarios.Add(ComVinculo("inativo@x.com", ativo: false, empresaId));
        await _db.SaveChangesAsync();

        var data = await new AdminDashboardQueries(_db, _assinaturaRepo).ObterAsync(DateTime.UtcNow);

        data.TotalUsuariosAtivos.Should().Be(1);
    }

    [Fact]
    public async Task ReceitaMensal_vem_do_predicado_canonico_de_MRR()
    {
        // F3 (#754): o dashboard delega o MRR à fonte única SomarPrecoMensalAtivasAsync,
        // mesma definição de MetricasFinanceirasUseCase e RevenueMetricsQueries. Antes, somava
        // db.AssinaturasEmpresa inline (predicado divergente) sem passar pela porta.
        _assinaturaRepo.SomarPrecoMensalAtivasAsync(null, Arg.Any<CancellationToken>())
            .Returns(149.90m);

        // Isca de regressão: assinatura Ativa com plano de preço DIVERGENTE no db. Se alguém
        // reintroduzir o SUM inline (db.AssinaturasEmpresa...), o resultado vira 999 e falha.
        var plano = new Plano { Id = Guid.NewGuid(), Nome = "Isca", PrecoMensal = 999m, Ativo = true, CriadoEm = DateTime.UtcNow };
        _db.Planos.Add(plano);
        _db.AssinaturasEmpresa.Add(new AssinaturaEmpresa
        {
            Id = Guid.NewGuid(), EmpresaId = Guid.NewGuid(), PlanoId = plano.Id,
            Status = StatusAssinatura.Ativa, DataInicio = DateTime.UtcNow.AddDays(-30),
            CriadoEm = DateTime.UtcNow.AddDays(-30), AlteradoEm = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        var data = await new AdminDashboardQueries(_db, _assinaturaRepo).ObterAsync(DateTime.UtcNow);

        data.ReceitaMensalEstimada.Should().Be(149.90m);
    }

    private static Usuario SemVinculo(string email, bool ativo) => new()
    {
        Id = Guid.NewGuid(), Nome = email, Email = email, SenhaHash = "x", Ativo = ativo
    };

    private static Usuario ComVinculo(string email, bool ativo, Guid empresaId)
    {
        var u = SemVinculo(email, ativo);
        u.Empresas.Add(new UsuarioEmpresa
        {
            Id = Guid.NewGuid(), UsuarioId = u.Id, EmpresaId = empresaId, Ativo = true, CriadoEm = DateTime.UtcNow
        });
        return u;
    }

    public void Dispose() => _db.Dispose();
}

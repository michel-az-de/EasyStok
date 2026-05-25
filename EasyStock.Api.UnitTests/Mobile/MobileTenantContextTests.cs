using EasyStock.Application.Ports.Output;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Mobile;

/// <summary>
/// Trava o mecanismo do fix de RLS no sync mobile: sem principal JWT (auth por
/// X-Mobile-Api-Key) o DbContext.CurrentTenantId cairia em Guid.Empty e a policy
/// RLS (fail-closed) zeraria o reverse-pull (web→mobile) e bloquearia writes ERP.
/// SetMobileTenantContext(deviceEmpresaId) é o que o SetTenantOnConnectionInterceptor
/// usa para emitir "SET app.empresa_id" correto — liberando exatamente aquele tenant.
/// </summary>
public class MobileTenantContextTests
{
    private static EasyStockDbContext NewDb(ICurrentUserAccessor currentUser) =>
        new(new DbContextOptionsBuilder<EasyStockDbContext>().Options, currentUser);

    [Fact]
    public void CurrentTenantId_SemPrincipalJwt_EhGuidEmpty()
    {
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.IsAuthenticated.Returns(false);
        using var db = NewDb(currentUser);

        // É a raiz do bug: sem este fix o sync mobile rodava com Guid.Empty.
        db.CurrentTenantId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void SetMobileTenantContext_DefineTenantDaConexao_SemJwt()
    {
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.IsAuthenticated.Returns(false);
        using var db = NewDb(currentUser);
        var deviceTenant = Guid.NewGuid();

        db.SetMobileTenantContext(deviceTenant);

        db.CurrentTenantId.Should().Be(deviceTenant);
    }

    [Fact]
    public void OverrideMobile_TemPrecedencia_SobreOClaimJwt()
    {
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.EmpresaId.Returns(Guid.NewGuid());
        using var db = NewDb(currentUser);
        var deviceTenant = Guid.NewGuid();

        db.SetMobileTenantContext(deviceTenant);

        db.CurrentTenantId.Should().Be(deviceTenant);
    }
}

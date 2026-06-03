using System.Reflection;
using EasyStock.Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>
/// Trava a politica de autorizacao dos controllers de diagnostico (issue #442).
/// <see cref="DiagnosticoController"/> e <see cref="DiagnosticoLogsController"/>
/// servem dados de PLATAFORMA cross-tenant (logs de arquivo, system-errors,
/// export, dashboard com logs em modo HTML) e DEVEM exigir <c>SuperAdmin</c> —
/// nao <c>Admin</c>, que tambem aceita o admin-de-tenant (todo tenant cria seu
/// usuario primario como <c>NivelAcesso.Admin</c>).
///
/// <para>
/// <see cref="DiagnosticoInfraController"/> permanece em <c>Admin</c> de
/// proposito: e o card de saude per-empresa, escopado por tenant via
/// <c>ICurrentUserAccessor</c>. Este teste trava o split intencional.
/// </para>
/// </summary>
public class DiagnosticoAuthorizationTests
{
    [Theory]
    [InlineData(typeof(DiagnosticoController))]
    [InlineData(typeof(DiagnosticoLogsController))]
    public void ControllersDeDiagnosticoPlataforma_ExigemSuperAdmin(Type controllerType)
    {
        PolicyDeAutorizacao(controllerType).Should().Be(
            "SuperAdmin",
            $"{controllerType.Name} expoe diagnostico/logs de toda a plataforma e nao pode ser acessivel por admin-de-tenant");
    }

    [Fact]
    public void DiagnosticoInfraController_PermaneceEmAdmin()
    {
        PolicyDeAutorizacao(typeof(DiagnosticoInfraController)).Should().Be(
            "Admin",
            "card de saude per-empresa e escopado por tenant — fica acessivel ao admin do proprio tenant");
    }

    private static string? PolicyDeAutorizacao(Type controllerType) =>
        controllerType.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Select(a => a.Policy)
            .SingleOrDefault();
}

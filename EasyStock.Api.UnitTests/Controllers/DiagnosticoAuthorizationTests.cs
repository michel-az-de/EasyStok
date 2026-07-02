using System.Reflection;
using EasyStock.Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>
/// Trava a politica de autorizacao dos controllers de diagnostico (issue #442, #763).
/// <see cref="DiagnosticoController"/>, <see cref="DiagnosticoLogsController"/> e
/// <see cref="DiagnosticoInfraController"/> servem dados de PLATAFORMA cross-tenant
/// (logs de arquivo, system-errors, export, dashboard, enumeracao de empresas via
/// <c>HealthEmpresas</c>, texto de queries via <c>pg_stat_statements</c>, mutacoes de
/// estado global) e DEVEM exigir <c>SuperAdmin</c> — nao <c>Admin</c>, que tambem
/// aceita o admin-de-tenant (todo tenant cria seu usuario primario como
/// <c>NivelAcesso.Admin</c>).
///
/// <para>
/// A tabela <c>empresas</c> tem chave <c>Id</c> (nao <c>EmpresaId</c>), logo escapa
/// tanto do filtro global de tenant quanto da RLS — a autorizacao e a unica barreira.
/// Ate #763, <see cref="DiagnosticoInfraController"/> estava em <c>Admin</c> sob a
/// premissa (falsa) de ser escopado por tenant.
/// </para>
/// </summary>
public class DiagnosticoAuthorizationTests
{
    [Theory]
    [InlineData(typeof(DiagnosticoController))]
    [InlineData(typeof(DiagnosticoLogsController))]
    [InlineData(typeof(DiagnosticoInfraController))]
    public void ControllersDeDiagnosticoPlataforma_ExigemSuperAdmin(Type controllerType)
    {
        PolicyDeAutorizacao(controllerType).Should().Be(
            "SuperAdmin",
            $"{controllerType.Name} expoe diagnostico/logs de toda a plataforma e nao pode ser acessivel por admin-de-tenant");
    }

    private static string? PolicyDeAutorizacao(Type controllerType) =>
        controllerType.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Select(a => a.Policy)
            .SingleOrDefault();
}

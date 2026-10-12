using System.Security.Claims;
using EasyStock.Application.Reporting;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Async.Reporting;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Services;

public class WorkerCurrentUserAccessorTests
{
    [Theory]
    [InlineData("permissoesExplicitas", "true")]
    [InlineData("permissao", "ConfigurarSla")]
    public void UsuarioHttpComListaVaziaOuLegada_NaoRecebeAcessoDoSistema(string tipo, string valor)
    {
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("nivel", "SuperAdmin"), new Claim(tipo, valor)], "teste"))
        }};
        var accessor = new WorkerCurrentUserAccessor(http, Substitute.For<IReportExecutionScope>());
        foreach (var permissao in Enum.GetValues<Permissao>().Except(EasyStock.Domain.Services.PermissoesLegadas.Valores))
            accessor.TemPermissao(permissao).Should().BeFalse();
    }

    [Fact]
    public void ContextoDeSistemaPreservaPermissoesDosJobs()
    {
        var accessor = new WorkerCurrentUserAccessor(null, Substitute.For<IReportExecutionScope>());
        foreach (var permissao in Enum.GetValues<Permissao>())
            accessor.TemPermissao(permissao).Should().BeTrue();
    }
}

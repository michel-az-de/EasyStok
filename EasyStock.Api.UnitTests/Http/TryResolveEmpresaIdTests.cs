using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Http;

/// <summary>
/// #1434: o SuperAdmin do console entra com o claim <c>empresaId</c> da empresa padrão e não
/// manda <c>?empresaId=</c>. Sem a query, a empresa do token vale; a query explícita continua
/// tendo precedência; sem query e sem claim, o 400 continua.
/// </summary>
public class TryResolveEmpresaIdTests
{
    private static readonly Guid EmpresaDoToken = Guid.NewGuid();

    private sealed class ControllerDeTeste : EasyStockControllerBase
    {
        public bool Resolver(ICurrentUserAccessor usuario, Guid? pedida, out Guid empresaId, out IActionResult? erro) =>
            TryResolveEmpresaId(usuario, pedida, out empresaId, out erro);
    }

    private static ICurrentUserAccessor SuperAdmin(Guid empresaDoToken)
    {
        var usuario = Substitute.For<ICurrentUserAccessor>();
        usuario.Nivel.Returns(NivelAcesso.SuperAdmin);
        usuario.EmpresaId.Returns(empresaDoToken);
        return usuario;
    }

    [Fact]
    public void SuperAdmin_SemQuery_UsaEmpresaDoToken()
    {
        var ok = new ControllerDeTeste().Resolver(SuperAdmin(EmpresaDoToken), null, out var empresaId, out var erro);

        ok.Should().BeTrue();
        erro.Should().BeNull();
        empresaId.Should().Be(EmpresaDoToken);
    }

    [Fact]
    public void SuperAdmin_ComQuery_UsaAQuery()
    {
        var outra = Guid.NewGuid();

        var ok = new ControllerDeTeste().Resolver(SuperAdmin(EmpresaDoToken), outra, out var empresaId, out _);

        ok.Should().BeTrue();
        empresaId.Should().Be(outra);
    }

    [Fact]
    public void SuperAdmin_SemQueryESemClaim_DevolveBadRequest()
    {
        var ok = new ControllerDeTeste().Resolver(SuperAdmin(Guid.Empty), null, out _, out var erro);

        ok.Should().BeFalse();
        erro.Should().BeOfType<BadRequestObjectResult>();
    }
}

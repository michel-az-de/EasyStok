using EasyStock.Api.Controllers;
using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Authorization;

public sealed class ClientesFotoControllerTests
{
    [Theory]
    [InlineData(NivelAcesso.Operador)]
    [InlineData(NivelAcesso.Gerente)]
    [InlineData(NivelAcesso.SuperAdmin)]
    public async Task EmpresaDivergente_E400AntesDeAcessarFotoEmTodasAsAcoes(NivelAcesso nivel)
    {
        var usuario = Substitute.For<ICurrentUserAccessor>();
        usuario.Nivel.Returns(nivel);
        usuario.EmpresaId.Returns(Guid.NewGuid());
        await ConferirRecusa(new ClientesFotoController(null!, usuario), Guid.NewGuid());
    }

    [Theory]
    [InlineData(NivelAcesso.Operador)]
    [InlineData(NivelAcesso.SuperAdmin)]
    public async Task JwtSemEmpresa_E400AntesDeAcessarFotoEmTodasAsAcoes(NivelAcesso nivel)
    {
        var usuario = Substitute.For<ICurrentUserAccessor>();
        usuario.Nivel.Returns(nivel);
        usuario.EmpresaId.Returns(Guid.Empty);
        await ConferirRecusa(new ClientesFotoController(null!, usuario), null);
    }

    private static async Task ConferirRecusa(ClientesFotoController controller, Guid? empresa)
    {
        var cliente = Guid.NewGuid();
        (await controller.Obter(cliente, empresa, CancellationToken.None)).Should().BeOfType<BadRequestObjectResult>();
        (await controller.Enviar(cliente, null!, empresa, CancellationToken.None)).Should().BeOfType<BadRequestObjectResult>();
        (await controller.Remover(cliente, empresa, CancellationToken.None)).Should().BeOfType<BadRequestObjectResult>();
    }
}

using EasyStock.Api.Authorization;
using EasyStock.Api.Controllers;
using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Authorization;

public sealed class ClientesFreteControllerTests
{
    [Theory]
    [InlineData(NivelAcesso.Operador)]
    [InlineData(NivelAcesso.SuperAdmin)]
    public async Task EmpresaDiferenteDaSessao_E400MesmoParaSuperAdmin(NivelAcesso nivel)
    {
        var usuario = Substitute.For<ICurrentUserAccessor>();
        usuario.EmpresaId.Returns(Guid.NewGuid());
        usuario.Nivel.Returns(nivel);
        var controller = new ClientesFreteController(null!, usuario);
        var resposta = await controller.Consultar(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);
        resposta.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public void EndpointExigeOperadorEModuloAtendimento()
    {
        var tipo = typeof(ClientesFreteController);
        tipo.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()
            .Should().ContainSingle(a => a.Policy == "Operador");
        ModulosConvention.ModulosDe(tipo, nameof(ClientesFreteController.Consultar))
            .Should().Equal(Modulo.Atendimento);
    }
}

using EasyStock.Api.Authorization;
using EasyStock.Api.Controllers;
using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Authorization;

public sealed class ClientesNotasControllerTests
{
    [Theory]
    [InlineData(NivelAcesso.Operador)]
    [InlineData(NivelAcesso.SuperAdmin)]
    public async Task NovasLeiturasSemEmpresaJwtRecusamAntesDoUseCase(NivelAcesso nivel)
    {
        var usuario = Substitute.For<ICurrentUserAccessor>();
        usuario.EmpresaId.Returns(Guid.Empty);
        usuario.Nivel.Returns(nivel);
        var controller = new ClientesCrmController(null!, null!, null!, null!, null!, null!, null!, null!, usuario);
        (await controller.PaginaNotas(Guid.NewGuid(), CancellationToken.None)).Should().BeOfType<BadRequestObjectResult>();
        (await controller.ObterNota(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None)).Should().BeOfType<BadRequestObjectResult>();
    }

    [Theory]
    [InlineData(nameof(ClientesCrmController.PaginaNotas))]
    [InlineData(nameof(ClientesCrmController.ObterNota))]
    public void NovasLeiturasExigemOperadorEModuloAtendimentoSemParametroDeEmpresa(string acao)
    {
        var metodo = typeof(ClientesCrmController).GetMethod(acao)!;
        metodo.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Should().ContainSingle(a => a.Policy == "Operador");
        ModulosConvention.ModulosDe(typeof(ClientesCrmController), acao).Should().Equal(Modulo.Atendimento);
        metodo.GetParameters().Should().NotContain(p => p.Name == "empresaId");
    }
}

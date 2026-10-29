using EasyStock.Api.Authorization;
using EasyStock.Api.Controllers;
using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Authorization;

public sealed class ClientesViagemControllerTests
{
    [Theory]
    [InlineData(NivelAcesso.Operador)]
    [InlineData(NivelAcesso.SuperAdmin)]
    public async Task EmpresaDivergente_E400AntesDeConsultarViagem(NivelAcesso nivel)
    {
        var usuario = Substitute.For<ICurrentUserAccessor>();
        usuario.EmpresaId.Returns(Guid.NewGuid());
        usuario.Nivel.Returns(nivel);
        var resposta = await new ClientesViagemController(null!, usuario).Consultar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);
        resposta.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task SemEmpresaNoJwt_NaoAceitaEmpresaDaQuery()
    {
        var resposta = await new ClientesViagemController(null!, Substitute.For<ICurrentUserAccessor>())
            .Consultar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);
        resposta.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task SemEntregas_NaoExibeEnderecosDasParadas()
    {
        var usuario = Substitute.For<ICurrentUserAccessor>();
        usuario.EmpresaId.Returns(Guid.NewGuid());
        usuario.TemPermissao(Permissao.AcessarModuloAtendimento).Returns(true);
        var resposta = await new ClientesViagemController(null!, usuario).Consultar(Guid.NewGuid(), Guid.NewGuid(), null, CancellationToken.None);
        resposta.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public void ExigeOperadorEAtendimento_EntregasEConferidaNaAction()
    {
        var tipo = typeof(ClientesViagemController);
        tipo.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()
            .Should().ContainSingle(a => a.Policy == "Operador");
        ModulosConvention.ModulosDe(tipo, nameof(ClientesViagemController.Consultar))!.Should().Equal(Modulo.Atendimento);
    }
}

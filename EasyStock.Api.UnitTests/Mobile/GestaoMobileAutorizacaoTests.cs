using System.Reflection;
using EasyStock.Api.Mobile.Controllers;
using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Mobile;

/// <summary>
/// #1540: as rotas de gestão do mobile ficam fora do filtro global e do RLS (tabelas mobile_*),
/// então a guarda do controller é a única barreira entre empresas e entre perfis.
/// </summary>
public class GestaoMobileAutorizacaoTests
{
    [Fact]
    public async Task Dashboard_TokenSemEmpresa_NaoEscolheEmpresaPelaQuery()
    {
        var resposta = await Controller(NivelAcesso.Visualizador, Guid.Empty)
            .GetDashboard(Guid.NewGuid(), lojaId: null, CancellationToken.None);

        resposta.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task DevicesHealth_TokenSemEmpresa_NaoEscolheEmpresaPelaQuery()
    {
        var resposta = await Controller(NivelAcesso.Visualizador, Guid.Empty)
            .GetDevicesHealth(Guid.NewGuid(), CancellationToken.None);

        resposta.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Dashboard_OutraEmpresa_Nega()
    {
        var resposta = await Controller(NivelAcesso.Gerente, Guid.NewGuid())
            .GetDashboard(Guid.NewGuid(), lojaId: null, CancellationToken.None);

        resposta.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(403);
    }

    [Theory]
    [InlineData(nameof(DeviceBackupController.List))]
    [InlineData(nameof(DeviceBackupController.Download))]
    public void BackupDoAparelho_ExigeGerente(string acao) =>
        typeof(DeviceBackupController).GetMethod(acao)!
            .GetCustomAttributes<AuthorizeAttribute>().Select(a => a.Policy)
            .Should().Contain("Gerente");

    // A guarda responde antes de qualquer consulta: banco e broker não são tocados.
    private static OperationController Controller(NivelAcesso nivel, Guid empresaDoToken)
    {
        var usuario = Substitute.For<ICurrentUserAccessor>();
        usuario.Nivel.Returns(nivel);
        usuario.EmpresaId.Returns(empresaDoToken);
        return new OperationController(
            db: null!, eventBroker: null!, usuario,
            new ConfigurationBuilder().Build(), NullLogger<OperationController>.Instance);
    }
}

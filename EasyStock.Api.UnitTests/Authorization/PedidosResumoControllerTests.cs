using EasyStock.Api.Authorization;
using EasyStock.Api.Controllers;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Pedidos;
using EasyStock.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Authorization;

/// <summary>#3294: o resumo de Pedidos tem a mesma porta da lista: Operador, módulo Atendimento e só a empresa do token.</summary>
public sealed class PedidosResumoControllerTests
{
    [Fact]
    public void ExigeOperadorEModuloAtendimento()
    {
        var metodo = typeof(PedidosResumoController).GetMethod(nameof(PedidosResumoController.Get))!;
        metodo.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Should().ContainSingle(a => a.Policy == "Operador");
        ModulosConvention.ModulosDe(typeof(PedidosResumoController), nameof(PedidosResumoController.Get)).Should().Equal(Modulo.Atendimento);
    }

    [Fact]
    public async Task OperadorDaEmpresaAPedindoBRecebe403SemConsultar()
    {
        var empresaA = Guid.NewGuid();
        var usuario = Substitute.For<ICurrentUserAccessor>();
        usuario.EmpresaId.Returns(empresaA);
        usuario.Nivel.Returns(NivelAcesso.Operador);
        var consulta = Substitute.For<IPedidosResumoQuery>();
        var controller = new PedidosResumoController(new ResumoPedidosUseCase(consulta, TimeProvider.System), usuario);

        (await controller.Get(Guid.NewGuid())).Should().BeOfType<ForbidResult>();
        await consulta.DidNotReceiveWithAnyArgs().ResumirAsync(default!, default, default);
    }

    [Fact]
    public async Task SemEmpresaNaQueryUsaADoToken()
    {
        var empresaA = Guid.NewGuid();
        var usuario = Substitute.For<ICurrentUserAccessor>();
        usuario.EmpresaId.Returns(empresaA);
        usuario.Nivel.Returns(NivelAcesso.Operador);
        var consulta = Substitute.For<IPedidosResumoQuery>();
        consulta.ResumirAsync(default!, default, default).ReturnsForAnyArgs(new ResumoDosPedidos(0, 0, 0m, 0m, 0, 0, 0, 0, 0, 0, 0m, 0, 0, []));
        var controller = new PedidosResumoController(new ResumoPedidosUseCase(consulta, TimeProvider.System), usuario);

        (await controller.Get(null)).Should().BeOfType<OkObjectResult>();
        await consulta.Received(1).ResumirAsync(Arg.Is<FiltroDosPedidos>(f => f.EmpresaId == empresaA), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }
}

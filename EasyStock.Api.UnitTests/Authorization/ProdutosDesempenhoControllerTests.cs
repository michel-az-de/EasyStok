using EasyStock.Api.Authorization;
using EasyStock.Api.Controllers;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.GerenciarProduto;
using EasyStock.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Authorization;

/// <summary>#3299: o desempenho dos produtos tem a porta da lista de produtos: módulo Cardápio, Operador e só a empresa do token.</summary>
public sealed class ProdutosDesempenhoControllerTests
{
    [Fact]
    public void ExigeOperadorEModuloCardapio()
    {
        var metodo = typeof(ProdutosDesempenhoController).GetMethod(nameof(ProdutosDesempenhoController.Get))!;
        metodo.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Should().ContainSingle(a => a.Policy == "Operador");
        ModulosConvention.ModulosDe(typeof(ProdutosDesempenhoController), nameof(ProdutosDesempenhoController.Get)).Should().Equal(Modulo.Cardapio);
    }

    [Fact]
    public async Task OperadorDaEmpresaAPedindoBRecebe403SemConsultar()
    {
        var (controller, consulta) = Montar(Guid.NewGuid());

        (await controller.Get(Guid.NewGuid())).Should().BeOfType<ForbidResult>();
        await consulta.DidNotReceiveWithAnyArgs().CatalogoAsync(default);
    }

    [Fact]
    public async Task SemEmpresaNaQueryUsaADoToken()
    {
        var empresaA = Guid.NewGuid();
        var (controller, consulta) = Montar(empresaA);

        (await controller.Get(null)).Should().BeOfType<OkObjectResult>();
        await consulta.Received(1).CatalogoAsync(empresaA, Arg.Any<CancellationToken>());
        await consulta.Received(1).VendasDesdeAsync(empresaA, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    private static (ProdutosDesempenhoController, IProdutosDesempenhoQuery) Montar(Guid empresa)
    {
        var usuario = Substitute.For<ICurrentUserAccessor>();
        usuario.EmpresaId.Returns(empresa);
        usuario.Nivel.Returns(NivelAcesso.Operador);
        var consulta = Substitute.For<IProdutosDesempenhoQuery>();
        consulta.CatalogoAsync(default).ReturnsForAnyArgs(new List<ProdutoDoCatalogo>());
        consulta.VendasDesdeAsync(default, default).ReturnsForAnyArgs(new List<VendaDoProdutoNoPedido>());
        consulta.HistoricoAsync(default).ReturnsForAnyArgs(new List<HistoricoDoProduto>());
        return (new ProdutosDesempenhoController(new DesempenhoDosProdutosUseCase(consulta, TimeProvider.System), usuario), consulta);
    }
}

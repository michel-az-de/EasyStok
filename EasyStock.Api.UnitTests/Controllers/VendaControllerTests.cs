using EasyStock.Api.Controllers;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit;

namespace EasyStock.Api.UnitTests.Controllers;

public class VendaControllerTests
{
    private readonly IVendaRepository _vendaRepository = Substitute.For<IVendaRepository>();
    private readonly VendaController _controller;

    public VendaControllerTests()
    {
        _controller = new VendaController(_vendaRepository);
    }

    [Fact]
    public async Task GetAll_DeveRetornarOk_ComListaDeVendas()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        var vendas = new List<Venda> { new Venda { Id = Guid.NewGuid(), EmpresaId = empresaId } };
        _vendaRepository.GetVendasPorEmpresaAsync(empresaId, 1, 20).Returns(((IEnumerable<Venda>)vendas, 1));

        // Act
        var result = await _controller.GetAll(empresaId);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var okResult = result as OkObjectResult;
        okResult!.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task GetById_DeveRetornarOk_QuandoVendaEncontrada()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        var venda = new Venda { Id = Guid.NewGuid(), EmpresaId = empresaId };
        _vendaRepository.GetByIdAsync(empresaId, venda.Id).Returns(venda);

        // Act
        var result = await _controller.GetById(venda.Id, empresaId);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var okResult = result as OkObjectResult;
        okResult!.Value.Should().Be(venda);
    }

    [Fact]
    public async Task GetById_DeveRetornarNotFound_QuandoVendaNaoEncontrada()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        var id = Guid.NewGuid();
        _vendaRepository.GetByIdAsync(empresaId, id).Returns((Venda?)null);

        // Act
        var result = await _controller.GetById(id, empresaId);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

}

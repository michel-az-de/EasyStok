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
        var vendas = new List<Venda> { new Venda { Id = Guid.NewGuid() } };
        _vendaRepository.GetAllAsync().Returns(vendas);

        // Act
        var result = await _controller.GetAll();

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var okResult = result as OkObjectResult;
        okResult!.Value.Should().BeEquivalentTo(vendas);
    }

    [Fact]
    public async Task GetById_DeveRetornarOk_QuandoVendaEncontrada()
    {
        // Arrange
        var venda = new Venda { Id = Guid.NewGuid() };
        _vendaRepository.GetByIdAsync(venda.Id).Returns(venda);

        // Act
        var result = await _controller.GetById(venda.Id);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var okResult = result as OkObjectResult;
        okResult!.Value.Should().Be(venda);
    }

    [Fact]
    public async Task GetById_DeveRetornarNotFound_QuandoVendaNaoEncontrada()
    {
        // Arrange
        var id = Guid.NewGuid();
        _vendaRepository.GetByIdAsync(id).Returns((Venda?)null);

        // Act
        var result = await _controller.GetById(id);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Create_DeveRetornarCreated_ComVenda()
    {
        // Arrange
        var venda = new Venda { Id = Guid.NewGuid() };

        // Act
        var result = await _controller.Create(venda);

        // Assert
        result.Should().BeOfType<CreatedAtActionResult>();
        var createdResult = result as CreatedAtActionResult;
        createdResult!.Value.Should().Be(venda);
        createdResult.ActionName.Should().Be("GetById");
    }

    [Fact]
    public async Task Update_DeveRetornarNoContent_QuandoSucesso()
    {
        // Arrange
        var venda = new Venda { Id = Guid.NewGuid() };

        // Act
        var result = await _controller.Update(venda.Id, venda);

        // Assert
        result.Should().BeOfType<NoContentResult>();
        await _vendaRepository.Received(1).UpdateAsync(venda);
    }

    [Fact]
    public async Task Update_DeveRetornarBadRequest_QuandoIdsNaoCoincidem()
    {
        // Arrange
        var venda = new Venda { Id = Guid.NewGuid() };

        // Act
        var result = await _controller.Update(Guid.NewGuid(), venda);

        // Assert
        result.Should().BeOfType<BadRequestResult>();
    }

    [Fact]
    public async Task Delete_DeveRetornarNoContent()
    {
        // Arrange
        var id = Guid.NewGuid();

        // Act
        var result = await _controller.Delete(id);

        // Assert
        result.Should().BeOfType<NoContentResult>();
        await _vendaRepository.Received(1).DeleteAsync(id);
    }
}

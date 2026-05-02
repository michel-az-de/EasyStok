using EasyStock.Api.Controllers;
using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.RegistrarSaidaEstoque;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace EasyStock.Api.UnitTests.Controllers;

public class VendaControllerTests
{
    private readonly IVendaRepository _vendaRepository = Substitute.For<IVendaRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly RegistrarSaidaEstoqueUseCase _registrarSaidaUseCase;
    private readonly VendaController _controller;

    public VendaControllerTests()
    {
        _currentUser.Nivel.Returns(NivelAcesso.SuperAdmin);
        _registrarSaidaUseCase = new RegistrarSaidaEstoqueUseCase(
            Substitute.For<IProdutoRepository>(),
            Substitute.For<IItemEstoqueRepository>(),
            _vendaRepository,
            Substitute.For<IItemVendaRepository>(),
            Substitute.For<IMovimentacaoEstoqueRepository>(),
            Substitute.For<IUnitOfWork>(),
            Substitute.For<ILogger<RegistrarSaidaEstoqueUseCase>>());
        _controller = new VendaController(_vendaRepository, _registrarSaidaUseCase, _currentUser);
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
        var envelope = ((OkObjectResult)result).Value.Should().BeOfType<ApiResponse<Venda>>().Subject;
        envelope.Data.Should().Be(venda);
    }

    [Fact]
    public async Task GetById_DeveRetornarNotFound_QuandoVendaNaoEncontrada()
    {
        var empresaId = Guid.NewGuid();
        var id = Guid.NewGuid();
        _vendaRepository.GetByIdAsync(empresaId, id).Returns((Venda?)null);

        var result = await _controller.GetById(id, empresaId);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

}

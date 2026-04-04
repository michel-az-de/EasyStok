using EasyStock.Api.Controllers;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.GerenciarLoja;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace EasyStock.Api.UnitTests.Controllers;

public class LojaControllerTests
{
    private readonly ILojaRepository _lojaRepository = Substitute.For<ILojaRepository>();
    private readonly IAssinaturaEmpresaRepository _assinaturaRepository = Substitute.For<IAssinaturaEmpresaRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ILogger<GerenciarLojaUseCase> _logger = Substitute.For<ILogger<GerenciarLojaUseCase>>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly GerenciarLojaUseCase _lojaUseCase;
    private readonly LojaController _controller;

    public LojaControllerTests()
    {
        _lojaUseCase = new GerenciarLojaUseCase(_lojaRepository, _assinaturaRepository, _unitOfWork, _logger);
        _currentUser.Nivel.Returns(NivelAcesso.SuperAdmin);
        _controller = new LojaController(_lojaUseCase, _currentUser);
    }

    [Fact]
    public async Task GetAll_DeveRetornarOk_QuandoEmpresaValida()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        var lojas = new List<Loja>
        {
            new Loja { Id = Guid.NewGuid(), EmpresaId = empresaId, Nome = "Loja Central", Ativa = true }
        };
        _lojaRepository.GetByEmpresaAsync(empresaId).Returns(lojas);

        // Act
        var result = await _controller.GetAll(empresaId);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var okResult = result as OkObjectResult;
        okResult!.Value.Should().NotBeNull();
        var lojaResults = okResult.Value as IEnumerable<LojaResult>;
        lojaResults.Should().NotBeNull();
        lojaResults!.Should().HaveCount(1);
    }

    [Fact]
    public async Task Create_DeveRetornarCreated_QuandoCommandValido()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        var command = new CriarLojaCommand(empresaId, "Nova Loja", null, null, null, null);

        _assinaturaRepository.GetAtivaAsync(empresaId).Returns((AssinaturaEmpresa?)null);
        _unitOfWork.CommitAsync().Returns(1);

        // Act
        var result = await _controller.Create(command);

        // Assert
        result.Should().BeOfType<CreatedResult>();
        var createdResult = result as CreatedResult;
        createdResult!.Value.Should().BeOfType<LojaResult>();
        var lojaResult = createdResult.Value as LojaResult;
        lojaResult!.Nome.Should().Be("Nova Loja");
        lojaResult.EmpresaId.Should().Be(empresaId);
    }
}

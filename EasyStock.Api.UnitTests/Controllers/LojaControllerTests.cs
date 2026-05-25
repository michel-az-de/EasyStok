using EasyStock.Api.Controllers;
using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.AtualizarLoja;
using EasyStock.Application.UseCases.CriarLoja;
using EasyStock.Application.UseCases.DesativarLoja;
using EasyStock.Application.UseCases.ReativarLoja;
using EasyStock.Application.UseCases.ListarLojas;
using EasyStock.Application.UseCases.Loja;
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
    private readonly ILogger<CriarLojaUseCase> _criarLogger = Substitute.For<ILogger<CriarLojaUseCase>>();
    private readonly ILogger<AtualizarLojaUseCase> _atualizarLogger = Substitute.For<ILogger<AtualizarLojaUseCase>>();
    private readonly ILogger<DesativarLojaUseCase> _desativarLogger = Substitute.For<ILogger<DesativarLojaUseCase>>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private readonly CriarLojaUseCase _criarUseCase;
    private readonly AtualizarLojaUseCase _atualizarUseCase;
    private readonly DesativarLojaUseCase _desativarUseCase;
    private readonly ListarLojasUseCase _listarUseCase;
    private readonly LojaController _controller;

    public LojaControllerTests()
    {
        _criarUseCase = new CriarLojaUseCase(_lojaRepository, _assinaturaRepository, _unitOfWork, _criarLogger);
        _atualizarUseCase = new AtualizarLojaUseCase(_lojaRepository, _unitOfWork, _atualizarLogger);
        _desativarUseCase = new DesativarLojaUseCase(_lojaRepository, _unitOfWork, _desativarLogger);
        var reativarUseCase = new ReativarLojaUseCase(_lojaRepository, _unitOfWork, Substitute.For<ILogger<ReativarLojaUseCase>>());
        _listarUseCase = new ListarLojasUseCase(_lojaRepository);

        _currentUser.Nivel.Returns(NivelAcesso.SuperAdmin);
        _controller = new LojaController(_criarUseCase, _atualizarUseCase, _desativarUseCase, reativarUseCase, _listarUseCase, _currentUser);
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
        var envelope = ((OkObjectResult)result).Value.Should().BeOfType<ApiResponse<IEnumerable<LojaResult>>>().Subject;
        envelope.Data.Should().HaveCount(1);
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
        var envelope = ((CreatedResult)result).Value.Should().BeOfType<ApiResponse<LojaResult>>().Subject;
        envelope.Data.Nome.Should().Be("Nova Loja");
        envelope.Data.EmpresaId.Should().Be(empresaId);
    }
}

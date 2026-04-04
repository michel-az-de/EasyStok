using EasyStock.Api.Controllers;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.GerenciarUsuario;
using EasyStock.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

public class UsuarioControllerTests
{
    private readonly IUsuarioRepository _usuarioRepository = Substitute.For<IUsuarioRepository>();
    private readonly IAssinaturaEmpresaRepository _assinaturaRepository = Substitute.For<IAssinaturaEmpresaRepository>();
    private readonly IUsuarioEmpresaRepository _usuarioEmpresaRepository = Substitute.For<IUsuarioEmpresaRepository>();
    private readonly IUsuarioPerfilRepository _usuarioPerfilRepository = Substitute.For<IUsuarioPerfilRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ILogger<GerenciarUsuarioUseCase> _logger = Substitute.For<ILogger<GerenciarUsuarioUseCase>>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly UsuarioController _controller;

    public UsuarioControllerTests()
    {
        var useCase = new GerenciarUsuarioUseCase(
            _usuarioRepository,
            _assinaturaRepository,
            _usuarioEmpresaRepository,
            _usuarioPerfilRepository,
            _unitOfWork,
            _logger);

        _currentUser.Nivel.Returns(NivelAcesso.SuperAdmin);
        _controller = new UsuarioController(useCase, _currentUser);
    }

    [Fact]
    public async Task Update_DeveRetornarBadRequest_QuandoIdDaRotaDifereDoBody()
    {
        var routeId = Guid.NewGuid();
        var bodyId = Guid.NewGuid();

        var result = await _controller.Update(routeId, new AtualizarUsuarioCommand(bodyId, "Nome", "email@x.com"));

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task AlterarSenha_DeveRetornarForbid_QuandoUsuarioNaoForODonoNemSuperAdmin()
    {
        _currentUser.Nivel.Returns(NivelAcesso.Operador);
        _currentUser.UsuarioId.Returns(Guid.NewGuid());

        var result = await _controller.AlterarSenha(Guid.NewGuid(), new AlterarSenhaRequest("atual", "nova"));

        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task AtribuirPerfil_DeveRetornarForbid_QuandoEmpresaForDiferenteDoUsuarioAtual()
    {
        _currentUser.Nivel.Returns(NivelAcesso.Admin);
        _currentUser.EmpresaId.Returns(Guid.NewGuid());

        var result = await _controller.AtribuirPerfil(Guid.NewGuid(), new AtribuirPerfilRequest(Guid.NewGuid(), Guid.NewGuid(), null));

        result.Should().BeOfType<ForbidResult>();
    }
}

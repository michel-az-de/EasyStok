using EasyStock.Api.Controllers;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.AlterarSenhaUsuario;
using EasyStock.Application.UseCases.AtribuirPerfilUsuario;
using EasyStock.Application.UseCases.AtualizarUsuario;
using EasyStock.Application.UseCases.CriarUsuario;
using EasyStock.Application.UseCases.DesativarUsuario;
using EasyStock.Application.UseCases.ListarUsuarios;
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
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private readonly CriarUsuarioUseCase _criarUseCase;
    private readonly AtualizarUsuarioUseCase _atualizarUseCase;
    private readonly AlterarSenhaUsuarioUseCase _alterarSenhaUseCase;
    private readonly DesativarUsuarioUseCase _desativarUseCase;
    private readonly ListarUsuariosUseCase _listarUseCase;
    private readonly AtribuirPerfilUsuarioUseCase _atribuirPerfilUseCase;
    private readonly UsuarioController _controller;

    public UsuarioControllerTests()
    {
        var criarLogger = Substitute.For<ILogger<CriarUsuarioUseCase>>();
        var atualizarLogger = Substitute.For<ILogger<AtualizarUsuarioUseCase>>();
        var alterarSenhaLogger = Substitute.For<ILogger<AlterarSenhaUsuarioUseCase>>();
        var desativarLogger = Substitute.For<ILogger<DesativarUsuarioUseCase>>();
        var atribuirPerfilLogger = Substitute.For<ILogger<AtribuirPerfilUsuarioUseCase>>();

        var passwordHasher = Substitute.For<IPasswordHasher>();
        _criarUseCase = new CriarUsuarioUseCase(_usuarioRepository, _assinaturaRepository, _usuarioEmpresaRepository, _usuarioPerfilRepository, _unitOfWork, passwordHasher, criarLogger);
        _atualizarUseCase = new AtualizarUsuarioUseCase(_usuarioRepository, _unitOfWork, atualizarLogger);
        _alterarSenhaUseCase = new AlterarSenhaUsuarioUseCase(_usuarioRepository, _unitOfWork, passwordHasher, alterarSenhaLogger);
        _desativarUseCase = new DesativarUsuarioUseCase(_usuarioRepository, _usuarioEmpresaRepository, _unitOfWork, desativarLogger);
        _listarUseCase = new ListarUsuariosUseCase(_usuarioRepository);
        _atribuirPerfilUseCase = new AtribuirPerfilUsuarioUseCase(_usuarioRepository, _usuarioPerfilRepository, _unitOfWork, atribuirPerfilLogger);

        _currentUser.Nivel.Returns(NivelAcesso.SuperAdmin);
        _controller = new UsuarioController(_criarUseCase, _atualizarUseCase, _alterarSenhaUseCase, _desativarUseCase, _listarUseCase, _atribuirPerfilUseCase, _currentUser);
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

using EasyStock.Api.Controllers;
using EasyStock.Api.Services;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.AlterarSenha;
using EasyStock.Application.UseCases.AutenticarUsuario;
using EasyStock.Application.UseCases.AtualizarUsuarioAtual;
using EasyStock.Application.UseCases.CadastrarUsuario;
using EasyStock.Application.UseCases.ConfirmEmail;
using EasyStock.Application.UseCases.EsqueciSenha;
using EasyStock.Application.UseCases.Logout;
using EasyStock.Application.UseCases.ObterUsuarioAtual;
using EasyStock.Application.UseCases.RefreshToken;
using EasyStock.Application.UseCases.ResetarSenha;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Exceptions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace EasyStock.Api.UnitTests.Controllers;

public class AuthControllerTests
{
    private readonly IUsuarioRepository _usuarioRepository = Substitute.For<IUsuarioRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IRefreshTokenRepository _refreshTokenRepository = Substitute.For<IRefreshTokenRepository>();
    private readonly IAuditLogRepository _auditLogRepository = Substitute.For<IAuditLogRepository>();
    private readonly ILogger<AutenticarUsuarioUseCase> _autenticarLogger = Substitute.For<ILogger<AutenticarUsuarioUseCase>>();
    private readonly EasyStock.Api.Services.IJwtTokenService _mockJwtService = Substitute.For<EasyStock.Api.Services.IJwtTokenService>();
    private readonly AutenticarUsuarioUseCase _autenticarUseCase;
    private readonly AuthController _controller;

    public AuthControllerTests()
    {
        _autenticarUseCase = new AutenticarUsuarioUseCase(_usuarioRepository, _unitOfWork, _autenticarLogger);

        _mockJwtService.GerarToken(Arg.Any<AutenticarUsuarioResult>()).Returns("mocked-jwt-token");
        _mockJwtService.GerarRefreshToken().Returns("mocked-refresh-token");
        _mockJwtService.ExpiresInSeconds.Returns(3600);

        _unitOfWork.CommitAsync().Returns(1);

        var usuarioRepo2 = Substitute.For<IUsuarioRepository>();
        var unitOfWork2 = Substitute.For<IUnitOfWork>();
        var refreshTokenRepo2 = Substitute.For<IRefreshTokenRepository>();
        var auditLogRepo2 = Substitute.For<IAuditLogRepository>();
        var resetTokenRepo = Substitute.For<IResetTokenRepository>();
        var currentUser = Substitute.For<ICurrentUserAccessor>();

        var cadastrarLogger = Substitute.For<ILogger<CadastrarUsuarioUseCase>>();
        var refreshTokenLogger = Substitute.For<ILogger<RefreshTokenUseCase>>();
        var logoutLogger = Substitute.For<ILogger<LogoutUseCase>>();
        var esqueciSenhaLogger = Substitute.For<ILogger<EsqueciSenhaUseCase>>();
        var resetarSenhaLogger = Substitute.For<ILogger<ResetarSenhaUseCase>>();
        var obterUsuarioAtualLogger = Substitute.For<ILogger<ObterUsuarioAtualUseCase>>();
        var atualizarUsuarioAtualLogger = Substitute.For<ILogger<AtualizarUsuarioAtualUseCase>>();
        var alterarSenhaLogger = Substitute.For<ILogger<AlterarSenhaUseCase>>();
        var jwtServiceApp = Substitute.For<EasyStock.Application.Ports.Output.IJwtTokenService>();

        var emailTokenRepo = Substitute.For<IEmailConfirmationTokenRepository>();
        var cadastrarUseCase = new CadastrarUsuarioUseCase(usuarioRepo2, auditLogRepo2, emailTokenRepo, null, unitOfWork2, cadastrarLogger);
        var refreshTokenUseCase = new RefreshTokenUseCase(refreshTokenRepo2, usuarioRepo2, auditLogRepo2, jwtServiceApp, unitOfWork2, refreshTokenLogger);
        var logoutUseCase = new LogoutUseCase(refreshTokenRepo2, auditLogRepo2, unitOfWork2, logoutLogger);
        var esqueciSenhaUseCase = new EsqueciSenhaUseCase(usuarioRepo2, resetTokenRepo, auditLogRepo2, unitOfWork2, esqueciSenhaLogger);
        var resetarSenhaUseCase = new ResetarSenhaUseCase(resetTokenRepo, refreshTokenRepo2, usuarioRepo2, auditLogRepo2, unitOfWork2, resetarSenhaLogger);
        var obterUsuarioAtualUseCase = new ObterUsuarioAtualUseCase(usuarioRepo2, currentUser, obterUsuarioAtualLogger);
        var atualizarUsuarioAtualUseCase = new AtualizarUsuarioAtualUseCase(usuarioRepo2, currentUser, unitOfWork2, atualizarUsuarioAtualLogger);
        var alterarSenhaUseCase = new AlterarSenhaUseCase(usuarioRepo2, currentUser, unitOfWork2, alterarSenhaLogger);
        var confirmEmailLogger = Substitute.For<ILogger<ConfirmEmailUseCase>>();
        var confirmEmailUseCase = new ConfirmEmailUseCase(emailTokenRepo, usuarioRepo2, auditLogRepo2, unitOfWork2, confirmEmailLogger);

        var exportarLogger = Substitute.For<ILogger<EasyStock.Application.UseCases.ExportarMeusDados.ExportarMeusDadosUseCase>>();
        var anonimizarLogger = Substitute.For<ILogger<EasyStock.Application.UseCases.AnonimizarMeusDados.AnonimizarMeusDadosUseCase>>();
        var usuarioEmpresaRepo = Substitute.For<IUsuarioEmpresaRepository>();
        var exportarUseCase = new EasyStock.Application.UseCases.ExportarMeusDados.ExportarMeusDadosUseCase(usuarioRepo2, usuarioEmpresaRepo, refreshTokenRepo2, currentUser, exportarLogger);
        var anonimizarUseCase = new EasyStock.Application.UseCases.AnonimizarMeusDados.AnonimizarMeusDadosUseCase(usuarioRepo2, refreshTokenRepo2, resetTokenRepo, emailTokenRepo, currentUser, unitOfWork2, anonimizarLogger);

        _controller = new AuthController(
            _autenticarUseCase,
            _mockJwtService,
            _refreshTokenRepository,
            _auditLogRepository,
            _unitOfWork,
            cadastrarUseCase,
            refreshTokenUseCase,
            logoutUseCase,
            esqueciSenhaUseCase,
            resetarSenhaUseCase,
            confirmEmailUseCase,
            obterUsuarioAtualUseCase,
            atualizarUsuarioAtualUseCase,
            alterarSenhaUseCase,
            exportarUseCase,
            anonimizarUseCase);
    }

    [Fact]
    public async Task Login_DevePropagar_CredenciaisInvalidasException_QuandoLoginFalha()
    {
        // Arrange
        _usuarioRepository.GetByEmailAsync(Arg.Any<string>()).Returns((Usuario?)null);

        var request = new LoginRequest("invalido@teste.com", "senhaErrada", null);

        // Act
        Func<Task> act = async () => await _controller.Login(request);

        // Assert
        await act.Should().ThrowAsync<CredenciaisInvalidasException>();
    }
}

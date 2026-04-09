using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.EsqueciSenha;
using EasyStock.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace EasyStock.Application.Tests.UseCases;

public class EsqueciSenhaUseCaseTests
{
    private readonly IUsuarioRepository _usuarioRepository = Substitute.For<IUsuarioRepository>();
    private readonly IResetTokenRepository _resetTokenRepository = Substitute.For<IResetTokenRepository>();
    private readonly IAuditLogRepository _auditLogRepository = Substitute.For<IAuditLogRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ILogger<EsqueciSenhaUseCase> _logger = Substitute.For<ILogger<EsqueciSenhaUseCase>>();
    private readonly IEmailService _emailService = Substitute.For<IEmailService>();

    private EsqueciSenhaUseCase CriarUseCase(bool comEmail = true) =>
        new(_usuarioRepository, _resetTokenRepository, _auditLogRepository, _unitOfWork, _logger,
            comEmail ? _emailService : null);

    private static Usuario CriarUsuario(string email = "user@empresa.com") =>
        new()
        {
            Id = Guid.NewGuid(),
            Nome = "Teste Usuario",
            Email = email,
            SenhaHash = BCrypt.Net.BCrypt.HashPassword("Senha@123"),
            Ativo = true,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        };

    [Fact]
    public async Task DeveGerarTokenERetornarSucesso_QuandoEmailValido()
    {
        var usuario = CriarUsuario();
        _usuarioRepository.GetByEmailAsync(usuario.Email).Returns(usuario);

        var useCase = CriarUseCase();
        var result = await useCase.ExecuteAsync(new EsqueciSenhaCommand(usuario.Email));

        result.Success.Should().BeTrue();
        await _resetTokenRepository.Received(1).AddAsync(Arg.Any<ResetToken>());
        await _unitOfWork.Received(1).CommitAsync();
    }

    [Fact]
    public async Task DeveRetornarSucessoSemRevelarEmail_QuandoEmailNaoExiste()
    {
        _usuarioRepository.GetByEmailAsync(Arg.Any<string>()).Returns((Usuario?)null);

        var useCase = CriarUseCase();
        var result = await useCase.ExecuteAsync(new EsqueciSenhaCommand("naoexiste@empresa.com"));

        // Deve retornar sucesso mesmo sem usuário para não revelar emails cadastrados
        result.Success.Should().BeTrue();
        await _resetTokenRepository.DidNotReceive().AddAsync(Arg.Any<ResetToken>());
        await _unitOfWork.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task DeveRetornarSucessoSemRevelarEmail_QuandoUsuarioInativo()
    {
        var usuario = CriarUsuario();
        usuario.Ativo = false;
        _usuarioRepository.GetByEmailAsync(usuario.Email).Returns(usuario);

        var useCase = CriarUseCase();
        var result = await useCase.ExecuteAsync(new EsqueciSenhaCommand(usuario.Email));

        result.Success.Should().BeTrue();
        await _resetTokenRepository.DidNotReceive().AddAsync(Arg.Any<ResetToken>());
    }

    [Fact]
    public async Task DeveEnviarEmailDeRecuperacao_QuandoEmailServiceConfigurado()
    {
        var usuario = CriarUsuario("envio@empresa.com");
        _usuarioRepository.GetByEmailAsync(usuario.Email).Returns(usuario);

        var useCase = CriarUseCase(comEmail: true);
        await useCase.ExecuteAsync(new EsqueciSenhaCommand(usuario.Email));

        await _emailService.Received(1).SendAsync(
            usuario.Email,
            Arg.Any<string>(),
            Arg.Any<string>());
    }

    [Fact]
    public async Task NaoDeveEnviarEmail_QuandoEmailServiceNaoConfigurado()
    {
        var usuario = CriarUsuario();
        _usuarioRepository.GetByEmailAsync(usuario.Email).Returns(usuario);

        var useCase = CriarUseCase(comEmail: false);
        await useCase.ExecuteAsync(new EsqueciSenhaCommand(usuario.Email));

        await _emailService.DidNotReceive().SendAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>());
    }

    [Fact]
    public async Task DeveAuditar_QuandoTokenGeradoComSucesso()
    {
        var usuario = CriarUsuario();
        _usuarioRepository.GetByEmailAsync(usuario.Email).Returns(usuario);

        var useCase = CriarUseCase();
        await useCase.ExecuteAsync(new EsqueciSenhaCommand(usuario.Email));

        await _auditLogRepository.Received(1).AddAsync(Arg.Any<AuditLog>());
    }

    [Fact]
    public async Task DeveRetornarSucessoMesmoSeEmailFalhar_QuandoSmtpLancaExcecao()
    {
        var usuario = CriarUsuario();
        _usuarioRepository.GetByEmailAsync(usuario.Email).Returns(usuario);
        _emailService
            .SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(Task.FromException(new InvalidOperationException("SMTP indisponivel")));

        var useCase = CriarUseCase(comEmail: true);

        // Deve continuar e retornar sucesso mesmo com falha no envio de email
        var result = await useCase.ExecuteAsync(new EsqueciSenhaCommand(usuario.Email));

        result.Success.Should().BeTrue();
        await _unitOfWork.Received(1).CommitAsync();
    }
}

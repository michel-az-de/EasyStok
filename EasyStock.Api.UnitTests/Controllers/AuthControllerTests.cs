using EasyStock.Api.Controllers;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.AutenticarUsuario;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Exceptions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace EasyStock.Api.UnitTests.Controllers;

public class AuthControllerTests
{
    private readonly IUsuarioRepository _usuarioRepository = Substitute.For<IUsuarioRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ILogger<AutenticarUsuarioUseCase> _logger = Substitute.For<ILogger<AutenticarUsuarioUseCase>>();
    private readonly IConfiguration _configuration;
    private readonly AutenticarUsuarioUseCase _autenticarUseCase;
    private readonly AuthController _controller;

    public AuthControllerTests()
    {
        _autenticarUseCase = new AutenticarUsuarioUseCase(_usuarioRepository, _unitOfWork, _logger);

        var inMemoryConfig = new Dictionary<string, string?>
        {
            ["Jwt:SecretKey"] = "test-secret-key-min-32-chars-here!!",
            ["Jwt:ExpirationMinutes"] = "60",
            ["Jwt:Issuer"] = "test-issuer",
            ["Jwt:Audience"] = "test-audience"
        };
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemoryConfig)
            .Build();

        _controller = new AuthController(_autenticarUseCase, _configuration);
    }

    [Fact]
    public async Task Login_DeveRetornarOk_QuandoCredenciaisValidas()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        var senha = "senha123";
        var senhaHash = BCrypt.Net.BCrypt.HashPassword(senha);

        var usuario = new Usuario
        {
            Id = Guid.NewGuid(),
            Nome = "Admin Teste",
            Email = "admin@teste.com",
            SenhaHash = senhaHash,
            Ativo = true,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        };

        _usuarioRepository.GetByEmailAsync("admin@teste.com").Returns(usuario);
        _unitOfWork.CommitAsync().Returns(1);

        var request = new LoginRequest("admin@teste.com", senha, null);

        // Act
        var result = await _controller.Login(request);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var okResult = result as OkObjectResult;
        okResult!.Value.Should().NotBeNull();

        var type = okResult.Value!.GetType();
        var tokenProp = type.GetProperty("token");
        tokenProp.Should().NotBeNull();
        var token = tokenProp!.GetValue(okResult.Value) as string;
        token.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Login_DeveRetornar401_QuandoCredenciaisInvalidas()
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

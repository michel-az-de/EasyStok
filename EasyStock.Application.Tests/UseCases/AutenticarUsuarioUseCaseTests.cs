using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.AutenticarUsuario;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Exceptions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace EasyStock.Application.Tests.UseCases;

public class AutenticarUsuarioUseCaseTests
{
    private static AutenticarUsuarioUseCase CriarUseCase(IUsuarioRepository usuarioRepository)
    {
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var logger = Substitute.For<ILogger<AutenticarUsuarioUseCase>>();
        return new AutenticarUsuarioUseCase(usuarioRepository, unitOfWork, logger);
    }

    [Fact]
    public async Task Autenticar_DeveRetornarResultado_QuandoCredenciaisValidas()
    {
        var usuarioId = Guid.NewGuid();
        var empresaId = Guid.NewGuid();
        var senhaHash = BCrypt.Net.BCrypt.HashPassword("senha123");

        var usuario = new Usuario
        {
            Id = usuarioId,
            Nome = "Joao Silva",
            Email = "joao@empresa.com",
            SenhaHash = senhaHash,
            Ativo = true,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow,
            Empresas = new List<UsuarioEmpresa>
            {
                new UsuarioEmpresa { Id = Guid.NewGuid(), UsuarioId = usuarioId, EmpresaId = empresaId, Ativo = true, CriadoEm = DateTime.UtcNow }
            }
        };

        var usuarioRepository = Substitute.For<IUsuarioRepository>();
        usuarioRepository.GetByEmailAsync("joao@empresa.com").Returns(usuario);

        var useCase = CriarUseCase(usuarioRepository);
        var command = new AutenticarUsuarioCommand("joao@empresa.com", "senha123", empresaId);

        var result = await useCase.ExecuteAsync(command);

        Assert.Equal(usuarioId, result.UsuarioId);
        Assert.Equal("Joao Silva", result.Nome);
        Assert.Equal("joao@empresa.com", result.Email);
    }

    [Fact]
    public async Task Autenticar_DeveLancarCredenciaisInvalidas_QuandoUsuarioNaoEncontrado()
    {
        var usuarioRepository = Substitute.For<IUsuarioRepository>();
        usuarioRepository.GetByEmailAsync(Arg.Any<string>()).Returns((Usuario?)null);

        var useCase = CriarUseCase(usuarioRepository);
        var command = new AutenticarUsuarioCommand("naoexiste@empresa.com", "senha123", null);

        await Assert.ThrowsAsync<CredenciaisInvalidasException>(() => useCase.ExecuteAsync(command));
    }

    [Fact]
    public async Task Autenticar_DeveLancarCredenciaisInvalidas_QuandoUsuarioInativo()
    {
        var usuario = new Usuario
        {
            Id = Guid.NewGuid(),
            Nome = "Maria",
            Email = "maria@empresa.com",
            SenhaHash = BCrypt.Net.BCrypt.HashPassword("senha123"),
            Ativo = false,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        };

        var usuarioRepository = Substitute.For<IUsuarioRepository>();
        usuarioRepository.GetByEmailAsync("maria@empresa.com").Returns(usuario);

        var useCase = CriarUseCase(usuarioRepository);
        var command = new AutenticarUsuarioCommand("maria@empresa.com", "senha123", null);

        await Assert.ThrowsAsync<CredenciaisInvalidasException>(() => useCase.ExecuteAsync(command));
    }

    [Fact]
    public async Task Autenticar_DeveLancarCredenciaisInvalidas_QuandoSenhaInvalida()
    {
        var usuario = new Usuario
        {
            Id = Guid.NewGuid(),
            Nome = "Carlos",
            Email = "carlos@empresa.com",
            SenhaHash = BCrypt.Net.BCrypt.HashPassword("senhaCorreta"),
            Ativo = true,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        };

        var usuarioRepository = Substitute.For<IUsuarioRepository>();
        usuarioRepository.GetByEmailAsync("carlos@empresa.com").Returns(usuario);

        var useCase = CriarUseCase(usuarioRepository);
        var command = new AutenticarUsuarioCommand("carlos@empresa.com", "senhaErrada", null);

        await Assert.ThrowsAsync<CredenciaisInvalidasException>(() => useCase.ExecuteAsync(command));
    }

    [Fact]
    public async Task Autenticar_DeveLancarCredenciaisInvalidas_QuandoEmpresaNaoVinculada()
    {
        var usuarioId = Guid.NewGuid();
        var outraEmpresaId = Guid.NewGuid();
        var empresaIdNaoVinculada = Guid.NewGuid();

        var usuario = new Usuario
        {
            Id = usuarioId,
            Nome = "Ana",
            Email = "ana@empresa.com",
            SenhaHash = BCrypt.Net.BCrypt.HashPassword("senha123"),
            Ativo = true,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow,
            Empresas = new List<UsuarioEmpresa>
            {
                new UsuarioEmpresa { Id = Guid.NewGuid(), UsuarioId = usuarioId, EmpresaId = outraEmpresaId, Ativo = true, CriadoEm = DateTime.UtcNow }
            }
        };

        var usuarioRepository = Substitute.For<IUsuarioRepository>();
        usuarioRepository.GetByEmailAsync("ana@empresa.com").Returns(usuario);

        var useCase = CriarUseCase(usuarioRepository);
        var command = new AutenticarUsuarioCommand("ana@empresa.com", "senha123", empresaIdNaoVinculada);

        await Assert.ThrowsAsync<CredenciaisInvalidasException>(() => useCase.ExecuteAsync(command));
    }
}

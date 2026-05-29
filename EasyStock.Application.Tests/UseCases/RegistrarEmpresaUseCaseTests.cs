using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.TestHelpers;
using EasyStock.Application.UseCases.RegistrarEmpresa;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases;

public class RegistrarEmpresaUseCaseTests
{
    private static RegistrarEmpresaUseCase CriarUseCase(
        IUsuarioRepository usuarioRepository,
        IPlanoRepository planoRepository,
        IPerfilRepository perfilRepository,
        IAssinaturaEmpresaRepository assinaturaRepository,
        IEmpresaRepository empresaRepository,
        IUsuarioEmpresaRepository usuarioEmpresaRepository,
        IUsuarioPerfilRepository usuarioPerfilRepository,
        IUnitOfWork unitOfWork)
    {
        var logger = Substitute.For<ILogger<RegistrarEmpresaUseCase>>();
        var hasher = new FakePasswordHasher();
        return new RegistrarEmpresaUseCase(
            usuarioRepository,
            planoRepository,
            perfilRepository,
            assinaturaRepository,
            empresaRepository,
            usuarioEmpresaRepository,
            usuarioPerfilRepository,
            unitOfWork,
            hasher,
            logger);
    }

    [Fact]
    public async Task Registrar_DeveCriarEmpresaEUsuario_QuandoDadosValidos()
    {
        var usuarioRepository = Substitute.For<IUsuarioRepository>();
        var planoRepository = Substitute.For<IPlanoRepository>();
        var perfilRepository = Substitute.For<IPerfilRepository>();
        var assinaturaRepository = Substitute.For<IAssinaturaEmpresaRepository>();
        var empresaRepository = Substitute.For<IEmpresaRepository>();
        var usuarioEmpresaRepository = Substitute.For<IUsuarioEmpresaRepository>();
        var usuarioPerfilRepository = Substitute.For<IUsuarioPerfilRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        usuarioRepository.GetByEmailAsync(Arg.Any<string>()).Returns((Usuario?)null);

        planoRepository.GetAtivosAsync().Returns(new List<Plano>
        {
            new Plano { Id = Guid.NewGuid(), Nome = "Starter", LimiteLojas = 1, LimiteUsuarios = 5, LimiteProdutos = 100, PrecoMensal = 49.90m, Ativo = true, CriadoEm = DateTime.UtcNow }
        });

        perfilRepository.GetPadroesAsync().Returns(new List<Perfil>
        {
            new Perfil { Id = Guid.NewGuid(), Nome = "Admin", CriadoEm = DateTime.UtcNow }
        });

        var useCase = CriarUseCase(usuarioRepository, planoRepository, perfilRepository, assinaturaRepository, empresaRepository, usuarioEmpresaRepository, usuarioPerfilRepository, unitOfWork);
        var command = new RegistrarEmpresaCommand("Empresa Teste", null, "Admin Teste", "admin@teste.com", "senha123");

        var result = await useCase.ExecuteAsync(command);

        Assert.NotEqual(Guid.Empty, result.EmpresaId);
        Assert.NotEqual(Guid.Empty, result.UsuarioId);
        await unitOfWork.Received(1).CommitAsync();
    }

    [Fact]
    public async Task Registrar_DeveLancarValidation_QuandoEmailJaCadastrado()
    {
        var usuarioRepository = Substitute.For<IUsuarioRepository>();
        var planoRepository = Substitute.For<IPlanoRepository>();
        var perfilRepository = Substitute.For<IPerfilRepository>();
        var assinaturaRepository = Substitute.For<IAssinaturaEmpresaRepository>();
        var empresaRepository = Substitute.For<IEmpresaRepository>();
        var usuarioEmpresaRepository = Substitute.For<IUsuarioEmpresaRepository>();
        var usuarioPerfilRepository = Substitute.For<IUsuarioPerfilRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        var usuarioExistente = new Usuario
        {
            Id = Guid.NewGuid(),
            Nome = "Existente",
            Email = "admin@teste.com",
            SenhaHash = "hash",
            Ativo = true,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        };
        usuarioRepository.GetByEmailAsync("admin@teste.com").Returns(usuarioExistente);

        var useCase = CriarUseCase(usuarioRepository, planoRepository, perfilRepository, assinaturaRepository, empresaRepository, usuarioEmpresaRepository, usuarioPerfilRepository, unitOfWork);
        var command = new RegistrarEmpresaCommand("Empresa Teste", null, "Admin Teste", "admin@teste.com", "senha123");

        await Assert.ThrowsAsync<UseCaseValidationException>(() => useCase.ExecuteAsync(command));
    }

    [Fact]
    public async Task Registrar_DeveLancarValidation_QuandoNenhumPlanoAtivo()
    {
        var usuarioRepository = Substitute.For<IUsuarioRepository>();
        var planoRepository = Substitute.For<IPlanoRepository>();
        var perfilRepository = Substitute.For<IPerfilRepository>();
        var assinaturaRepository = Substitute.For<IAssinaturaEmpresaRepository>();
        var empresaRepository = Substitute.For<IEmpresaRepository>();
        var usuarioEmpresaRepository = Substitute.For<IUsuarioEmpresaRepository>();
        var usuarioPerfilRepository = Substitute.For<IUsuarioPerfilRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        usuarioRepository.GetByEmailAsync(Arg.Any<string>()).Returns((Usuario?)null);
        planoRepository.GetAtivosAsync().Returns(new List<Plano>());

        var useCase = CriarUseCase(usuarioRepository, planoRepository, perfilRepository, assinaturaRepository, empresaRepository, usuarioEmpresaRepository, usuarioPerfilRepository, unitOfWork);
        var command = new RegistrarEmpresaCommand("Empresa Teste", null, "Admin Teste", "admin@teste.com", "senha123");

        await Assert.ThrowsAsync<UseCaseValidationException>(() => useCase.ExecuteAsync(command));
    }

    [Fact]
    public async Task Registrar_DeveCriarPerfilAdmin_QuandoNaoExisteTemplateGlobal()
    {
        var usuarioRepository = Substitute.For<IUsuarioRepository>();
        var planoRepository = Substitute.For<IPlanoRepository>();
        var perfilRepository = Substitute.For<IPerfilRepository>();
        var assinaturaRepository = Substitute.For<IAssinaturaEmpresaRepository>();
        var empresaRepository = Substitute.For<IEmpresaRepository>();
        var usuarioEmpresaRepository = Substitute.For<IUsuarioEmpresaRepository>();
        var usuarioPerfilRepository = Substitute.For<IUsuarioPerfilRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        usuarioRepository.GetByEmailAsync(Arg.Any<string>()).Returns((Usuario?)null);

        planoRepository.GetAtivosAsync().Returns(new List<Plano>
        {
            new Plano { Id = Guid.NewGuid(), Nome = "Starter", LimiteLojas = 1, LimiteUsuarios = 5, LimiteProdutos = 100, PrecoMensal = 49.90m, Ativo = true, CriadoEm = DateTime.UtcNow }
        });

        perfilRepository.GetPadroesAsync().Returns(new List<Perfil>());

        var useCase = CriarUseCase(usuarioRepository, planoRepository, perfilRepository, assinaturaRepository, empresaRepository, usuarioEmpresaRepository, usuarioPerfilRepository, unitOfWork);
        var command = new RegistrarEmpresaCommand("Empresa Teste", null, "Admin Teste", "admin@teste.com", "senha123");

        var result = await useCase.ExecuteAsync(command);

        Assert.NotEqual(Guid.Empty, result.EmpresaId);
        await perfilRepository.Received(1).AddAsync(Arg.Is<Perfil>(p => p.Nome == "Admin" && p.EmpresaId == result.EmpresaId));
    }
}

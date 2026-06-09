using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.TestHelpers;
using EasyStock.Application.UseCases.AutenticarUsuario;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>
/// Testes do step-1 do login 2-etapas (ADR-0031): valida credenciais e
/// retorna lista de empresas SEM emitir token.
///
/// Cobertura:
/// - Credenciais inválidas (usuário inexistente / senha errada / inativo).
/// - SuperAdmin → IsSuperAdmin=true, empresas vazia (login direto sem seleção).
/// - Tenant 1 empresa → retorna 1 (mesmo com 1, exige seleção explícita — decisão de UX).
/// - Tenant N empresas → retorna N ordenadas por nome.
/// - Empresa inativa do vínculo não aparece.
/// </summary>
public class ListarEmpresasParaLoginUseCaseTests
{
    private static ListarEmpresasParaLoginUseCase CriarUseCase(IUsuarioRepository usuarioRepository)
    {
        var hasher = new FakePasswordHasher();
        var logger = Substitute.For<ILogger<ListarEmpresasParaLoginUseCase>>();
        return new ListarEmpresasParaLoginUseCase(usuarioRepository, hasher, logger);
    }

    private static UsuarioEmpresa Vinculo(Guid usuarioId, Guid empresaId, string nomeEmpresa, bool ativo = true)
        => new()
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuarioId,
            EmpresaId = empresaId,
            Ativo = ativo,
            CriadoEm = DateTime.UtcNow,
            Empresa = new Empresa
            {
                Id = empresaId,
                Nome = nomeEmpresa,
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow,
            }
        };

    [Fact]
    public async Task DeveLancarCredenciaisInvalidas_QuandoUsuarioNaoEncontrado()
    {
        var repo = Substitute.For<IUsuarioRepository>();
        repo.GetByEmailAsync(Arg.Any<string>()).Returns((Usuario?)null);

        var useCase = CriarUseCase(repo);

        await Assert.ThrowsAsync<CredenciaisInvalidasException>(
            () => useCase.ExecuteAsync(new ListarEmpresasParaLoginCommand("x@y.com", "senha123")));
    }

    [Fact]
    public async Task DeveLancarCredenciaisInvalidas_QuandoSenhaErrada()
    {
        var usuario = new Usuario
        {
            Id = Guid.NewGuid(),
            Nome = "Carlos",
            Email = "carlos@empresa.com",
            SenhaHash = FakePasswordHasher.MakeHash("senhaCorreta"),
            Ativo = true,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow,
        };
        var repo = Substitute.For<IUsuarioRepository>();
        repo.GetByEmailAsync(usuario.Email).Returns(usuario);

        var useCase = CriarUseCase(repo);

        await Assert.ThrowsAsync<CredenciaisInvalidasException>(
            () => useCase.ExecuteAsync(new ListarEmpresasParaLoginCommand(usuario.Email, "senhaErrada")));
    }

    [Fact]
    public async Task DeveRetornarIsSuperAdmin_QuandoUsuarioTemPerfilGlobal()
    {
        var usuarioId = Guid.NewGuid();
        var perfilSuperId = Guid.NewGuid();
        var usuario = new Usuario
        {
            Id = usuarioId,
            Nome = "Super Admin",
            Email = "admin@easystok.com",
            SenhaHash = FakePasswordHasher.MakeHash("senha123"),
            Ativo = true,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow,
            Empresas = new List<UsuarioEmpresa>(),
            Perfis = new List<UsuarioPerfil>
            {
                new UsuarioPerfil
                {
                    Id = Guid.NewGuid(),
                    UsuarioId = usuarioId,
                    EmpresaId = Guid.Empty,
                    PerfilId = perfilSuperId,
                    AtribuidoEm = DateTime.UtcNow,
                    Perfil = new Perfil
                    {
                        Id = perfilSuperId,
                        Nome = "SuperAdmin",
                        EmpresaId = null,
                        Nivel = NivelAcesso.SuperAdmin,
                        Permissoes = new List<PerfilPermissao>()
                    }
                }
            }
        };
        var repo = Substitute.For<IUsuarioRepository>();
        repo.GetByEmailAsync(usuario.Email).Returns(usuario);

        var useCase = CriarUseCase(repo);
        var result = await useCase.ExecuteAsync(new ListarEmpresasParaLoginCommand(usuario.Email, "senha123"));

        Assert.True(result.IsSuperAdmin);
        Assert.Empty(result.Empresas);
    }

    [Fact]
    public async Task DeveRetornarUmaEmpresa_QuandoTenantTemUmVinculo()
    {
        var usuarioId = Guid.NewGuid();
        var empresaId = Guid.NewGuid();
        var usuario = new Usuario
        {
            Id = usuarioId,
            Nome = "Thatiane",
            Email = "thati@casadababa.com",
            SenhaHash = FakePasswordHasher.MakeHash("senha123"),
            Ativo = true,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow,
            Empresas = new List<UsuarioEmpresa> { Vinculo(usuarioId, empresaId, "Casa da Baba") },
        };
        var repo = Substitute.For<IUsuarioRepository>();
        repo.GetByEmailAsync(usuario.Email).Returns(usuario);

        var useCase = CriarUseCase(repo);
        var result = await useCase.ExecuteAsync(new ListarEmpresasParaLoginCommand(usuario.Email, "senha123"));

        Assert.False(result.IsSuperAdmin);
        Assert.Single(result.Empresas);
        Assert.Equal(empresaId, result.Empresas[0].Id);
        Assert.Equal("Casa da Baba", result.Empresas[0].Nome);
    }

    [Fact]
    public async Task DeveRetornarEmpresasOrdenadasPorNome_QuandoTenantTemVarias()
    {
        var usuarioId = Guid.NewGuid();
        var idZelda = Guid.NewGuid();
        var idAbel = Guid.NewGuid();
        var usuario = new Usuario
        {
            Id = usuarioId,
            Nome = "Multi",
            Email = "multi@empresa.com",
            SenhaHash = FakePasswordHasher.MakeHash("senha123"),
            Ativo = true,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow,
            Empresas = new List<UsuarioEmpresa>
            {
                Vinculo(usuarioId, idZelda, "Zelda Massas"),
                Vinculo(usuarioId, idAbel, "Abel Doces"),
            },
        };
        var repo = Substitute.For<IUsuarioRepository>();
        repo.GetByEmailAsync(usuario.Email).Returns(usuario);

        var useCase = CriarUseCase(repo);
        var result = await useCase.ExecuteAsync(new ListarEmpresasParaLoginCommand(usuario.Email, "senha123"));

        Assert.Equal(2, result.Empresas.Count);
        Assert.Equal("Abel Doces", result.Empresas[0].Nome);   // ordenado A→Z
        Assert.Equal("Zelda Massas", result.Empresas[1].Nome);
    }

    [Fact]
    public async Task NaoDeveRetornarEmpresaInativa()
    {
        var usuarioId = Guid.NewGuid();
        var idAtiva = Guid.NewGuid();
        var idInativa = Guid.NewGuid();
        var usuario = new Usuario
        {
            Id = usuarioId,
            Nome = "User",
            Email = "user@empresa.com",
            SenhaHash = FakePasswordHasher.MakeHash("senha123"),
            Ativo = true,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow,
            Empresas = new List<UsuarioEmpresa>
            {
                Vinculo(usuarioId, idAtiva, "Empresa Ativa", ativo: true),
                Vinculo(usuarioId, idInativa, "Empresa Inativa", ativo: false),
            },
        };
        var repo = Substitute.For<IUsuarioRepository>();
        repo.GetByEmailAsync(usuario.Email).Returns(usuario);

        var useCase = CriarUseCase(repo);
        var result = await useCase.ExecuteAsync(new ListarEmpresasParaLoginCommand(usuario.Email, "senha123"));

        Assert.Single(result.Empresas);
        Assert.Equal(idAtiva, result.Empresas[0].Id);
    }
}

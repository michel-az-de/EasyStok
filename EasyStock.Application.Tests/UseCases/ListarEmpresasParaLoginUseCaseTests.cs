using EasyStock.Application.Ports.Output;
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
    private static ListarEmpresasParaLoginUseCase CriarUseCase(
        IUsuarioRepository usuarioRepository,
        IUnitOfWork? unitOfWork = null,
        IPasswordHasher? hasher = null)
    {
        var logger = Substitute.For<ILogger<ListarEmpresasParaLoginUseCase>>();
        return new ListarEmpresasParaLoginUseCase(
            usuarioRepository, unitOfWork ?? new FakeUnitOfWork(), hasher ?? new FakePasswordHasher(), logger);
    }

    private static Usuario UsuarioDeSenha(string senha, int falhas = 0) => new()
    {
        Id = Guid.NewGuid(),
        Nome = "Carlos",
        Email = "carlos@empresa.com",
        SenhaHash = FakePasswordHasher.MakeHash(senha),
        Ativo = true,
        FailedLoginAttempts = falhas,
        CriadoEm = DateTime.UtcNow,
        AlteradoEm = DateTime.UtcNow,
    };

    private static IUsuarioRepository RepoCom(Usuario usuario)
    {
        var repo = Substitute.For<IUsuarioRepository>();
        repo.GetByEmailAsync(usuario.Email).Returns(usuario);
        return repo;
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

    // ── #1352 (N7): o passo 1 conta a falha de senha com o mesmo bloqueio do login ─────────────

    [Fact]
    public async Task SenhaErradaIncrementaAsTentativas()
    {
        var usuario = UsuarioDeSenha("senhaCorreta");
        var repo = RepoCom(usuario);
        var unitOfWork = new FakeUnitOfWork();

        await Assert.ThrowsAsync<CredenciaisInvalidasException>(
            () => CriarUseCase(repo, unitOfWork).ExecuteAsync(new ListarEmpresasParaLoginCommand(usuario.Email, "senhaErrada")));

        usuario.FailedLoginAttempts.Should().Be(1);
        await repo.Received(1).UpdateAsync(usuario);
        unitOfWork.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task QuintaFalhaBloqueiaAConta()
    {
        // Aceite: 5 senhas erradas no passo 1; a 6ª, mesmo com a senha certa, é recusada por 15 min
        // no passo 1 e no login completo.
        var usuario = UsuarioDeSenha("senhaCorreta");
        var repo = RepoCom(usuario);
        var passo1 = CriarUseCase(repo);
        var login = new AutenticarUsuarioUseCase(
            repo, new FakeUnitOfWork(), new FakePasswordHasher(), Substitute.For<ILogger<AutenticarUsuarioUseCase>>());

        for (var falha = 1; falha <= 5; falha++)
        {
            await Assert.ThrowsAsync<CredenciaisInvalidasException>(
                () => passo1.ExecuteAsync(new ListarEmpresasParaLoginCommand(usuario.Email, "senhaErrada")));
        }

        usuario.FailedLoginAttempts.Should().Be(5);
        usuario.LockoutEnd.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(15), TimeSpan.FromSeconds(5));

        var recusaPasso1 = await Assert.ThrowsAsync<CredenciaisInvalidasException>(
            () => passo1.ExecuteAsync(new ListarEmpresasParaLoginCommand(usuario.Email, "senhaCorreta")));
        var recusaLogin = await Assert.ThrowsAsync<CredenciaisInvalidasException>(
            () => login.ExecuteAsync(new AutenticarUsuarioCommand(usuario.Email, "senhaCorreta", null)));

        recusaPasso1.Message.Should().Be("Conta bloqueada temporariamente.");
        recusaLogin.Message.Should().Be("Conta bloqueada temporariamente.");
    }

    [Fact]
    public async Task ContaBloqueadaRecusaSemVerificarASenha()
    {
        var usuario = UsuarioDeSenha("senhaCorreta", falhas: 5);
        usuario.LockoutEnd = DateTime.UtcNow.AddMinutes(10);
        var hasher = Substitute.For<IPasswordHasher>();
        var unitOfWork = new FakeUnitOfWork();

        var recusa = await Assert.ThrowsAsync<CredenciaisInvalidasException>(
            () => CriarUseCase(RepoCom(usuario), unitOfWork, hasher)
                .ExecuteAsync(new ListarEmpresasParaLoginCommand(usuario.Email, "senhaCorreta")));

        recusa.Message.Should().Be("Conta bloqueada temporariamente.");
        hasher.DidNotReceiveWithAnyArgs().Verify(default!, default!);
        usuario.FailedLoginAttempts.Should().Be(5, "tentar de novo bloqueado não estende a contagem");
        unitOfWork.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task SenhaCertaNoPasso1NaoZeraOContador()
    {
        var usuario = UsuarioDeSenha("senhaCorreta", falhas: 3);
        var repo = RepoCom(usuario);
        var unitOfWork = new FakeUnitOfWork();

        var resultado = await CriarUseCase(repo, unitOfWork)
            .ExecuteAsync(new ListarEmpresasParaLoginCommand(usuario.Email, "senhaCorreta"));

        resultado.IsSuperAdmin.Should().BeFalse();
        usuario.FailedLoginAttempts.Should().Be(3, "quem zera é o login completo");
        await repo.DidNotReceive().UpdateAsync(Arg.Any<Usuario>());
        unitOfWork.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task BloqueioVencidoRecomecaAContagemNoPasso1()
    {
        var usuario = UsuarioDeSenha("senhaCorreta", falhas: 5);
        usuario.LockoutEnd = DateTime.UtcNow.AddMinutes(-1);

        await Assert.ThrowsAsync<CredenciaisInvalidasException>(
            () => CriarUseCase(RepoCom(usuario))
                .ExecuteAsync(new ListarEmpresasParaLoginCommand(usuario.Email, "senhaErrada")));

        usuario.FailedLoginAttempts.Should().Be(1);
        usuario.EstaBloqueado().Should().BeFalse("a falha herdada de uma janela vencida não bloqueia de novo");
    }
}

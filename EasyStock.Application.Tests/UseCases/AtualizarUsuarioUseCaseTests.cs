using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.AtualizarUsuario;
using EasyStock.TestHelpers;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>
/// Trava o isolamento multi-tenant do PUT /api/usuarios/{id} (#764). Usuario nao
/// tem EmpresaId (escapa do filtro global + RLS) e o repositorio le com bypass de
/// RLS (reuso do fluxo pre-auth), entao a guarda de tenant tem que viver no use case.
/// </summary>
public class AtualizarUsuarioUseCaseTests
{
    private readonly IUsuarioRepository _repo = Substitute.For<IUsuarioRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private AtualizarUsuarioUseCase CriarUseCase() =>
        new(_repo, _currentUser, new FakeUnitOfWork(), Substitute.For<ILogger<AtualizarUsuarioUseCase>>());

    private static Usuario UsuarioDaEmpresa(Guid usuarioId, Guid empresaId) => new()
    {
        Id = usuarioId,
        Nome = "Alvo",
        Email = "alvo@empresa.com",
        SenhaHash = "hash",
        Ativo = true,
        CriadoEm = DateTime.UtcNow,
        AlteradoEm = DateTime.UtcNow,
        Empresas = new List<UsuarioEmpresa>
        {
            new() { Id = Guid.NewGuid(), UsuarioId = usuarioId, EmpresaId = empresaId, Ativo = true, CriadoEm = DateTime.UtcNow }
        }
    };

    [Fact]
    public async Task Admin_DeTenant_NaoAltera_Usuario_DeOutroTenant()
    {
        var alvoId = Guid.NewGuid();
        var empresaDoAlvo = Guid.NewGuid();
        _repo.GetByIdAsync(alvoId).Returns(UsuarioDaEmpresa(alvoId, empresaDoAlvo));
        _currentUser.Nivel.Returns(NivelAcesso.Admin);
        _currentUser.EmpresaId.Returns(Guid.NewGuid()); // tenant DIFERENTE do alvo

        var useCase = CriarUseCase();
        var command = new AtualizarUsuarioCommand(alvoId, "Novo Nome", "atacante@evil.com");

        var ex = await Assert.ThrowsAsync<UseCaseValidationException>(() => useCase.ExecuteAsync(command));
        ex.Message.Should().Be("Usuario nao encontrado."); // nao confirma existencia cross-tenant
        await _repo.DidNotReceive().UpdateAsync(Arg.Any<Usuario>());
    }

    [Fact]
    public async Task Admin_DeTenant_Altera_Usuario_DoProprioTenant()
    {
        var alvoId = Guid.NewGuid();
        var empresa = Guid.NewGuid();
        _repo.GetByIdAsync(alvoId).Returns(UsuarioDaEmpresa(alvoId, empresa));
        _currentUser.Nivel.Returns(NivelAcesso.Admin);
        _currentUser.EmpresaId.Returns(empresa); // MESMO tenant do alvo

        var useCase = CriarUseCase();
        await useCase.ExecuteAsync(new AtualizarUsuarioCommand(alvoId, "Nome Atualizado", null));

        await _repo.Received(1).UpdateAsync(Arg.Is<Usuario>(u => u.Nome == "Nome Atualizado"));
    }

    [Fact]
    public async Task SuperAdmin_Altera_Usuario_DeQualquerTenant()
    {
        var alvoId = Guid.NewGuid();
        _repo.GetByIdAsync(alvoId).Returns(UsuarioDaEmpresa(alvoId, Guid.NewGuid()));
        _currentUser.Nivel.Returns(NivelAcesso.SuperAdmin);
        _currentUser.EmpresaId.Returns(Guid.NewGuid()); // irrelevante para SuperAdmin

        var useCase = CriarUseCase();
        await useCase.ExecuteAsync(new AtualizarUsuarioCommand(alvoId, "Nome SuperAdmin", null));

        await _repo.Received(1).UpdateAsync(Arg.Is<Usuario>(u => u.Nome == "Nome SuperAdmin"));
    }
}

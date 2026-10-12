using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Auth;
using EasyStock.Application.Tests.Services.Auth;
using EasyStock.Application.UseCases.AtualizarUsuario;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.TestHelpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>
/// Trava o isolamento multi-tenant do PUT /api/usuarios/{id} (#764). Usuario nao
/// tem EmpresaId (escapa do filtro global + RLS) e o repositorio le com bypass de
/// RLS (reuso do fluxo pre-auth), entao a guarda de tenant tem que viver no use case.
/// </summary>
public class AtualizarUsuarioUseCaseTests
{
    private readonly TrocaDeContatoFixture _f = new();
    private readonly IAuditLogRepository _auditoria = Substitute.For<IAuditLogRepository>();
    private IUsuarioRepository _repo => _f.Usuarios;
    private ICurrentUserAccessor _currentUser => _f.UsuarioAtual;

    private readonly FakeResetTokenRepository _convitesGravados = new();
    private readonly IConsentimentoRepository _consentimentos = Substitute.For<IConsentimentoRepository>();

    private ConvitesDeAcesso Convites() => new(
        _convitesGravados, _f.Notificador, _consentimentos, Substitute.For<IEmpresaRepository>(),
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:LinkConvite"] = CenarioDeAcesso.LinkDoConvite }).Build(),
        new FakeTimeProvider(new DateTimeOffset(2026, 10, 2, 17, 30, 0, TimeSpan.Zero)),
        Substitute.For<ILogger<ConvitesDeAcesso>>());

    private AtualizarUsuarioUseCase CriarUseCase() =>
        new(_repo, _currentUser, _f.UnitOfWork, _f.Servico, Convites(), _auditoria, Substitute.For<ILogger<AtualizarUsuarioUseCase>>());

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

    // ── N4: troca de e-mail pelo Admin ─────────────────────────────────────────────────────────

    private Usuario AlvoDaEmpresa(Guid empresaId, params Guid[] outrasEmpresas)
    {
        var alvoId = Guid.NewGuid();
        var alvo = UsuarioDaEmpresa(alvoId, empresaId);
        foreach (var outra in outrasEmpresas)
            alvo.Empresas.Add(new UsuarioEmpresa { Id = Guid.NewGuid(), UsuarioId = alvoId, EmpresaId = outra, Ativo = true, CriadoEm = DateTime.UtcNow });
        _repo.GetByIdAsync(alvoId).Returns(alvo);
        return alvo;
    }

    private void ComoAdmin(Guid empresaId)
    {
        _currentUser.Nivel.Returns(NivelAcesso.Admin);
        _currentUser.EmpresaId.Returns(empresaId);
        _currentUser.UsuarioId.Returns(Guid.NewGuid());
    }

    [Fact]
    public async Task AdminNaoTrocaEmailDeUsuarioDeDuasEmpresas()
    {
        var empresaA = _f.EmpresaId;
        var alvo = AlvoDaEmpresa(empresaA, Guid.NewGuid());
        ComoAdmin(empresaA);

        var acao = () => CriarUseCase().ExecuteAsync(new AtualizarUsuarioCommand(alvo.Id, "Alvo", "novo@empresa.com"));

        await acao.Should().ThrowAsync<UsuarioNaoAutorizadoException>().WithMessage("*próprio usuário ou ao suporte*");
        alvo.EmailPendente.Should().BeNull();
        alvo.Email.Should().Be("alvo@empresa.com");
        _f.Eventos.Should().BeEmpty();
        await _repo.DidNotReceive().UpdateAsync(Arg.Any<Usuario>());
    }

    [Fact]
    public async Task AdminNaoTrocaEmailDeSuperAdmin()
    {
        var empresaA = _f.EmpresaId;
        var alvo = AlvoDaEmpresa(empresaA);
        alvo.Perfis.Add(new UsuarioPerfil
        {
            Id = Guid.NewGuid(), UsuarioId = alvo.Id, EmpresaId = Guid.Empty, PerfilId = Guid.NewGuid(),
            Perfil = new Perfil { Id = Guid.NewGuid(), Nome = "SuperAdmin", Nivel = NivelAcesso.SuperAdmin }
        });
        ComoAdmin(empresaA);

        var acao = () => CriarUseCase().ExecuteAsync(new AtualizarUsuarioCommand(alvo.Id, "Alvo", "novo@empresa.com"));

        await acao.Should().ThrowAsync<UsuarioNaoAutorizadoException>();
        alvo.EmailPendente.Should().BeNull();
        _f.Eventos.Should().BeEmpty();
    }

    [Fact]
    public async Task AdminTrocaEmailDeUsuarioExclusivoPeloFluxoPendente()
    {
        var empresaA = _f.EmpresaId;
        var alvo = AlvoDaEmpresa(empresaA);
        ComoAdmin(empresaA);

        await CriarUseCase().ExecuteAsync(new AtualizarUsuarioCommand(alvo.Id, "Alvo Novo", "novo@empresa.com"));

        alvo.Email.Should().Be("alvo@empresa.com", "o e-mail só troca no clique do link enviado ao endereço novo");
        alvo.EmailPendente.Should().Be("novo@empresa.com");
        alvo.Nome.Should().Be("Alvo Novo");
        _f.Eventos.Select(e => e.Tipo).Should().BeEquivalentTo(
            [TipoEventoNotificacao.ConfirmacaoEmail, TipoEventoNotificacao.ContatoAlterado]);
        _f.Eventos.Single(e => e.Tipo == TipoEventoNotificacao.ConfirmacaoEmail).Payload["email"].GetString().Should().Be("novo@empresa.com");
        _f.Eventos.Single(e => e.Tipo == TipoEventoNotificacao.ContatoAlterado).Payload["email"].GetString().Should().Be("alvo@empresa.com");
        await _auditoria.Received(1).AddAsync(Arg.Is<AuditLog>(a => a.Acao == "admin-troca-email-solicitada"));
    }

    [Fact]
    public async Task AdminAindaAlteraSoONomeDeUsuarioDeDuasEmpresas()
    {
        var empresaA = _f.EmpresaId;
        var alvo = AlvoDaEmpresa(empresaA, Guid.NewGuid());
        ComoAdmin(empresaA);

        await CriarUseCase().ExecuteAsync(new AtualizarUsuarioCommand(alvo.Id, "So o Nome", null));
        await CriarUseCase().ExecuteAsync(new AtualizarUsuarioCommand(alvo.Id, "So o Nome", "alvo@empresa.com"));

        alvo.Nome.Should().Be("So o Nome");
        alvo.EmailPendente.Should().BeNull();
        _f.Eventos.Should().BeEmpty();
        await _repo.Received(2).UpdateAsync(Arg.Is<Usuario>(u => u.Nome == "So o Nome"));
    }

    [Fact]
    public async Task SuperAdminTrocaEmailCrossTenantPeloMesmoFluxoComAuditoria()
    {
        var alvo = AlvoDaEmpresa(Guid.NewGuid(), Guid.NewGuid());
        _currentUser.Nivel.Returns(NivelAcesso.SuperAdmin);
        _currentUser.UsuarioId.Returns(Guid.NewGuid());

        await CriarUseCase().ExecuteAsync(new AtualizarUsuarioCommand(alvo.Id, "Alvo", "novo@empresa.com"));

        alvo.Email.Should().Be("alvo@empresa.com");
        alvo.EmailPendente.Should().Be("novo@empresa.com");
        _f.Eventos.Should().HaveCount(2);
        await _auditoria.Received(1).AddAsync(Arg.Is<AuditLog>(a => a.Acao == "admin-troca-email-solicitada"));
    }
    // ── N9: o convidado nunca teve acesso, então a troca de e-mail é imediata e refaz o convite ─────────────

    private Usuario ConvidadoDaEmpresa(Guid empresaId)
    {
        var alvo = AlvoDaEmpresa(empresaId);
        alvo.SenhaHash = Usuario.MarcadorDeConvite + Guid.NewGuid().ToString("N");
        return alvo;
    }

    [Fact]
    public async Task EditarEmailDeConvidadoEhImediatoERevogaEReemite()
    {
        var empresaA = _f.EmpresaId;
        var alvo = ConvidadoDaEmpresa(empresaA);
        ComoAdmin(empresaA);
        await Convites().EmitirAsync(alvo, empresaA, false, null, null);
        var antigo = _convitesGravados.Linhas.Single();
        _f.Eventos.Clear();

        await CriarUseCase().ExecuteAsync(new AtualizarUsuarioCommand(alvo.Id, "Alvo", "novo@empresa.com"));

        alvo.Email.Should().Be("novo@empresa.com", "o convidado nunca teve acesso: sem EmailPendente");
        alvo.EmailPendente.Should().BeNull();
        alvo.EmailConfirmado.Should().BeFalse();
        antigo.Usado.Should().BeTrue("o convite do contato antigo morre");
        _convitesGravados.Linhas.Single(l => !l.Usado).Canal.Should().Be("Email");
        var evento = _f.Eventos.Should().ContainSingle().Subject;
        evento.Tipo.Should().Be(TipoEventoNotificacao.ConviteAcesso);
        evento.Payload["email"].GetString().Should().Be("novo@empresa.com");
        evento.EmpresaId.Should().Be(empresaA);
        await _auditoria.Received(1).AddAsync(Arg.Is<AuditLog>(a => a.Acao == "admin-troca-email-convidado"));
    }

    [Fact]
    public async Task EditarEmailDeConvidadoParaUmEmailQueJaExisteRecusa()
    {
        var empresaA = _f.EmpresaId;
        var alvo = ConvidadoDaEmpresa(empresaA);
        ComoAdmin(empresaA);
        _repo.GetByEmailAsync("ocupado@empresa.com").Returns(
            new Usuario { Id = Guid.NewGuid(), Nome = "Outro", Email = "ocupado@empresa.com", SenhaHash = "x" });

        var acao = () => CriarUseCase().ExecuteAsync(new AtualizarUsuarioCommand(alvo.Id, "Alvo", "ocupado@empresa.com"));

        await acao.Should().ThrowAsync<UseCaseValidationException>().WithMessage("Email ja cadastrado.");
        alvo.Email.Should().Be("alvo@empresa.com");
        _f.Eventos.Should().BeEmpty();
    }

    [Fact]
    public async Task EditarSoONomeDeConvidadoNaoMexeNoConvite()
    {
        var empresaA = _f.EmpresaId;
        var alvo = ConvidadoDaEmpresa(empresaA);
        ComoAdmin(empresaA);
        await Convites().EmitirAsync(alvo, empresaA, false, null, null);
        _f.Eventos.Clear();

        await CriarUseCase().ExecuteAsync(new AtualizarUsuarioCommand(alvo.Id, "Nome Novo", "alvo@empresa.com"));

        alvo.Nome.Should().Be("Nome Novo");
        _convitesGravados.Linhas.Should().ContainSingle().Which.Usado.Should().BeFalse();
        _f.Eventos.Should().BeEmpty();
    }

    [Fact]
    public async Task EditarEmailDeQuemJaAceitouNaoMexeNoConvite()
    {
        var empresaA = _f.EmpresaId;
        var alvo = AlvoDaEmpresa(empresaA); // hash de senha comum: já aceitou
        ComoAdmin(empresaA);
        var aberto = ResetToken.Criar(alvo.Id, "hash-qualquer", DateTime.UtcNow.AddHours(10), null, null,
            FinalidadeResetToken.Convite, canal: "Email");
        await _convitesGravados.AddAsync(aberto);

        await CriarUseCase().ExecuteAsync(new AtualizarUsuarioCommand(alvo.Id, "Alvo", "novo@empresa.com"));

        alvo.Email.Should().Be("alvo@empresa.com", "vale a regra da N4: dois passos");
        alvo.EmailPendente.Should().Be("novo@empresa.com");
        aberto.Usado.Should().BeFalse();
        _f.Eventos.Select(e => e.Tipo).Should().BeEquivalentTo(
            [TipoEventoNotificacao.ConfirmacaoEmail, TipoEventoNotificacao.ContatoAlterado])
            .And.NotContain(TipoEventoNotificacao.ConviteAcesso);
    }
}

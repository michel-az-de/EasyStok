using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Auth;
using EasyStock.Application.Tests.Services.Auth;
using EasyStock.Application.UseCases.AlterarSenha;
using EasyStock.Application.UseCases.AtribuirPerfilUsuario;
using EasyStock.Application.UseCases.AtualizarUsuarioAtual;
using EasyStock.Application.UseCases.AutenticarUsuario;
using EasyStock.Application.UseCases.ConfirmEmail;
using EasyStock.Application.UseCases.ContatoUsuario;
using EasyStock.Application.UseCases.DesativarUsuario;
using EasyStock.Application.UseCases.ResetarSenha;
using EasyStock.Application.Validators;
using EasyStock.Domain.ValueObjects;
using EasyStock.TestHelpers;
using Microsoft.Extensions.Logging;
using AlterarSenhaUsuarioCommand = EasyStock.Application.UseCases.AlterarSenhaUsuario.AlterarSenhaCommand;
using AlterarSenhaUsuarioUseCase = EasyStock.Application.UseCases.AlterarSenhaUsuario.AlterarSenhaUsuarioUseCase;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>
/// #1352 (N7): reset de senha, troca de senha (própria e por admin), desativação e troca de perfil derrubam
/// as sessões do usuário na hora (carimbo + refresh tokens + cache). Login nunca derruba: logar o tablet
/// não pode tirar o balcão do ar.
/// </summary>
public class RevogacaoDeSessaoTests
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 2, 13, 45, 10, 789, TimeSpan.Zero);
    private static readonly DateTime Corte = new(2026, 10, 2, 13, 45, 10, DateTimeKind.Utc);

    private readonly IUsuarioRepository _usuarios = Substitute.For<IUsuarioRepository>();
    private readonly IRefreshTokenRepository _refreshTokens = Substitute.For<IRefreshTokenRepository>();
    private readonly ICacheService _cache = Substitute.For<ICacheService>();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly RevogadorSessoes _revogador;
    private readonly Usuario _usuario;

    public RevogacaoDeSessaoTests()
    {
        _revogador = new RevogadorSessoes(
            _usuarios, _refreshTokens, _cache, new FakeTimeProvider(Agora), Substitute.For<ILogger<RevogadorSessoes>>());
        _usuario = Usuario.Criar("Ana", "ana@casadababa.com", FakePasswordHasher.MakeHash("SenhaAntiga@123"));
        _usuarios.GetByIdAsync(_usuario.Id).Returns(_usuario);
    }

    /// <summary>
    /// #2070: a política lê o ator do banco. A dona da empresa, com perfil de nível Admin e a marca, faz as escritas de
    /// hierarquia destes testes; a revogação que eles provam é a do alvo.
    /// </summary>
    private (ICurrentUserAccessor Atual, HierarquiaDeUsuarios Hierarquia) Dona(Guid empresaId)
    {
        var dona = Usuario.Criar("Baba", "baba@exemplo.test", "hash");
        CenarioDeAcesso.DarPerfil(dona, empresaId, NivelAcesso.Admin, dona: true);
        _usuarios.GetByIdAsync(dona.Id).Returns(dona);
        var atual = Substitute.For<ICurrentUserAccessor>();
        atual.UsuarioId.Returns(dona.Id);
        atual.EmpresaId.Returns(empresaId);
        atual.Nivel.Returns(NivelAcesso.Admin);
        var vinculos = Substitute.For<IUsuarioEmpresaRepository>();
        vinculos.ContarDonasAtivasAsync(empresaId, Arg.Any<CancellationToken>()).Returns(1);
        var hierarquia = new HierarquiaDeUsuarios(_usuarios, vinculos, atual, Substitute.For<IAuditLogRepository>(), _unitOfWork,
            Substitute.For<ILogger<HierarquiaDeUsuarios>>());
        return (atual, hierarquia);
    }

    private async Task DeveTerRevogadoOUsuarioTodo()
    {
        _usuario.SessoesValidasDesde.Should().Be(Corte);
        await _usuarios.Received(1).AtualizarSessoesValidasDesdeAsync(_usuario.Id, Corte);
        await _refreshTokens.Received(1).RevogarSessoesAtivasAsync(_usuario.Id, Agora.UtcDateTime);
        await _cache.Received(1).RemoveAsync(CacheKeys.Sessao(_usuario.Id));
        _unitOfWork.CommitCount.Should().Be(1, "o commit continua sendo do use case");
    }

    private async Task NaoDeveTerRevogadoNada()
    {
        _usuario.SessoesValidasDesde.Should().BeNull();
        await _usuarios.DidNotReceiveWithAnyArgs().AtualizarSessoesValidasDesdeAsync(default, default);
        await _refreshTokens.DidNotReceiveWithAnyArgs().RevogarSessoesAtivasAsync(default, default);
        await _cache.DidNotReceiveWithAnyArgs().RemoveAsync(default(string)!);
    }

    // ── reset de senha ────────────────────────────────────────────────────────────────────────

    private (ResetarSenhaUseCase UseCase, IResetTokenRepository Tokens, ResetToken Token, IAuditLogRepository Auditoria) ResetarSenha(bool tokenValido = true)
    {
        var resetTokens = Substitute.For<IResetTokenRepository>();
        var token = ResetToken.Criar(_usuario.Id, "hash-do-token", Agora.UtcDateTime.AddMinutes(30), null, null);
        resetTokens.GetByTokenAsync("token-do-email").Returns(tokenValido ? token : null);
        resetTokens.ConsumirAsync(token.Id, Arg.Any<DateTime>()).Returns(true);
        var auditoria = Substitute.For<IAuditLogRepository>();
        var relogio = new FakeTimeProvider(Agora);
        var concluidor = new ConcluidorDeReset(
            _usuarios, resetTokens, auditoria, _revogador, Substitute.For<INotificadorService>(),
            new EmpresaDoEventoAnonimo(
                Substitute.For<IEmpresaPadraoResolver>(), Substitute.For<ITenantContextAccessor>(),
                Substitute.For<ILogger<EmpresaDoEventoAnonimo>>()),
            new FakePasswordHasher(), relogio, Substitute.For<ILogger<ConcluidorDeReset>>());
        var useCase = new ResetarSenhaUseCase(
            resetTokens, _usuarios, concluidor, new LimitePedidosAcesso(_cache), _unitOfWork, relogio,
            Substitute.For<ILogger<ResetarSenhaUseCase>>());
        return (useCase, resetTokens, token, auditoria);
    }

    [Fact]
    public async Task ResetDeSenhaRevogaAsSessoes()
    {
        _refreshTokens.RevogarSessoesAtivasAsync(_usuario.Id, Agora.UtcDateTime).Returns(2);
        var (useCase, tokens, token, auditoria) = ResetarSenha();

        await useCase.ExecuteAsync(new ResetarSenhaCommand("token-do-email", "NovaSenha@123"));

        await DeveTerRevogadoOUsuarioTodo();
        _usuario.SenhaHash.Should().Be(FakePasswordHasher.MakeHash("NovaSenha@123"));
        await tokens.Received(1).ConsumirAsync(token.Id, Agora.UtcDateTime);
        await auditoria.Received(1).AddAsync(Arg.Is<AuditLog>(a => a.Detalhes!.Contains("2 refresh tokens revogados")));
        // O laço N+1 saiu de cena: nenhum refresh token é lido nem atualizado um a um.
        await _refreshTokens.DidNotReceiveWithAnyArgs().GetByUsuarioIdAsync(default);
        await _refreshTokens.DidNotReceiveWithAnyArgs().UpdateAsync(default!);
    }

    [Fact]
    public async Task ResetComTokenInvalidoNaoRevoga()
    {
        var (useCase, _, _, _) = ResetarSenha(tokenValido: false);

        await Assert.ThrowsAsync<RegraDeDominioVioladaException>(
            () => useCase.ExecuteAsync(new ResetarSenhaCommand("token-do-email", "NovaSenha@123")));

        await NaoDeveTerRevogadoNada();
        _unitOfWork.CommitCount.Should().Be(0);
    }

    // ── troca de senha, a própria e a do usuário pelo admin ───────────────────────────────────

    [Theory]
    [InlineData("AlterarSenha")]
    [InlineData("AlterarSenhaUsuario")]
    public async Task TrocaDeSenhaRevoga(string fluxo)
    {
        await TrocarSenhaAsync(fluxo, senhaAtual: "SenhaAntiga@123");

        await DeveTerRevogadoOUsuarioTodo();
        _usuario.SenhaHash.Should().Be(FakePasswordHasher.MakeHash("NovaSenha@456"));
    }

    [Theory]
    [InlineData("AlterarSenha")]
    [InlineData("AlterarSenhaUsuario")]
    public async Task SenhaAtualErradaNaoRevoga(string fluxo)
    {
        await Assert.ThrowsAnyAsync<Exception>(() => TrocarSenhaAsync(fluxo, senhaAtual: "SenhaErrada@999"));

        await NaoDeveTerRevogadoNada();
        _unitOfWork.CommitCount.Should().Be(0);
    }

    private Task TrocarSenhaAsync(string fluxo, string senhaAtual)
    {
        var hasher = new FakePasswordHasher();
        if (fluxo == "AlterarSenha")
        {
            var logado = Substitute.For<ICurrentUserAccessor>();
            logado.UsuarioId.Returns(_usuario.Id);
            return new AlterarSenhaUseCase(
                    _usuarios, logado, _revogador, _unitOfWork, hasher, Substitute.For<ILogger<AlterarSenhaUseCase>>())
                .ExecuteAsync(new AlterarSenhaCommand(senhaAtual, "NovaSenha@456"));
        }

        return new AlterarSenhaUsuarioUseCase(
                _usuarios, new AlterarSenhaUsuarioCommandValidator(), _revogador, _unitOfWork, hasher,
                Substitute.For<ILogger<AlterarSenhaUsuarioUseCase>>())
            .ExecuteAsync(new AlterarSenhaUsuarioCommand(_usuario.Id, senhaAtual, "NovaSenha@456"));
    }

    // ── desativação ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DesativarRevoga()
    {
        var empresaId = Guid.NewGuid();
        var vinculo = new UsuarioEmpresa { Id = Guid.NewGuid(), UsuarioId = _usuario.Id, EmpresaId = empresaId, Ativo = true, CriadoEm = DateTime.UtcNow };
        _usuario.Empresas.Add(vinculo);
        var vinculos = Substitute.For<IUsuarioEmpresaRepository>();
        vinculos.GetByUsuarioEEmpresaAsync(_usuario.Id, empresaId).Returns(vinculo);
        var (atual, hierarquia) = Dona(empresaId);

        await new DesativarUsuarioUseCase(
                _usuarios, vinculos, _revogador, new CenarioDeAcesso().Convites(), hierarquia, atual, Substitute.For<IAuditLogRepository>(), _unitOfWork,
                Substitute.For<ILogger<DesativarUsuarioUseCase>>())
            .ExecuteAsync(new DesativarUsuarioCommand(_usuario.Id, empresaId));

        vinculo.Ativo.Should().BeFalse();
        await DeveTerRevogadoOUsuarioTodo();
    }

    [Fact]
    public async Task DesativarSemVinculoNaoRevoga()
    {
        var vinculos = Substitute.For<IUsuarioEmpresaRepository>();
        var empresaId = Guid.NewGuid();
        var (atual, hierarquia) = Dona(empresaId);

        await Assert.ThrowsAsync<UseCaseValidationException>(() => new DesativarUsuarioUseCase(
                _usuarios, vinculos, _revogador, new CenarioDeAcesso().Convites(), hierarquia, atual, Substitute.For<IAuditLogRepository>(), _unitOfWork,
                Substitute.For<ILogger<DesativarUsuarioUseCase>>())
            .ExecuteAsync(new DesativarUsuarioCommand(_usuario.Id, empresaId)));

        await NaoDeveTerRevogadoNada();
    }

    // ── troca de perfil ───────────────────────────────────────────────────────────────────────

    private void VincularA(Guid empresaId, bool ativo = true) =>
        _usuario.Empresas.Add(new UsuarioEmpresa { Id = Guid.NewGuid(), UsuarioId = _usuario.Id, EmpresaId = empresaId, Ativo = ativo, CriadoEm = DateTime.UtcNow });

    private static IPerfilRepository PerfilComum(NivelAcesso nivel = NivelAcesso.Admin, Guid? empresaId = null)
    {
        var repo = Substitute.For<IPerfilRepository>();
        repo.GetByIdAsync(Arg.Any<Guid>()).Returns(new Perfil { Nivel = nivel, EmpresaId = empresaId });
        return repo;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AtribuirPerfil_DeveRecusarPerfilDeNivelSuperAdmin(bool global)
    {
        // Com o vínculo ativo, a recusa é pelo perfil (o global de suporte e o de empresa com nível SuperAdmin, B1).
        var empresaId = Guid.NewGuid();
        VincularA(empresaId);
        var uc = AtribuirPerfil(Substitute.For<IUsuarioPerfilRepository>(), empresaId,
            PerfilComum(NivelAcesso.SuperAdmin, global ? null : empresaId));

        var act = () => uc.ExecuteAsync(new AtribuirPerfilUsuarioCommand(_usuario.Id, empresaId, Guid.NewGuid(), null));

        (await act.Should().ThrowAsync<UseCaseValidationException>()).Which.Message.Should().Be("Perfil invalido.");
        await NaoDeveTerRevogadoNada();
    }

    [Fact]
    public async Task AtribuirPerfil_DeveRecusarPerfilDeOutraEmpresa()
    {
        var empresaId = Guid.NewGuid();
        VincularA(empresaId);
        var uc = AtribuirPerfil(Substitute.For<IUsuarioPerfilRepository>(), empresaId, PerfilComum(NivelAcesso.Admin, Guid.NewGuid()));

        var act = () => uc.ExecuteAsync(new AtribuirPerfilUsuarioCommand(_usuario.Id, empresaId, Guid.NewGuid(), null));

        (await act.Should().ThrowAsync<UseCaseValidationException>()).Which.Message.Should().Be("Perfil invalido.");
    }

    private AtribuirPerfilUsuarioUseCase AtribuirPerfil(IUsuarioPerfilRepository perfis, Guid empresaId, IPerfilRepository? perfil = null)
    {
        var (atual, hierarquia) = Dona(empresaId);
        return new(_usuarios, perfis, Substitute.For<IUsuarioEmpresaRepository>(), perfil ?? PerfilComum(), _revogador, hierarquia, atual,
            Substitute.For<IAuditLogRepository>(), _unitOfWork, Substitute.For<ILogger<AtribuirPerfilUsuarioUseCase>>());
    }

    [Fact]
    public async Task TrocaDePerfilRevoga()
    {
        var empresaId = Guid.NewGuid();
        var perfilId = Guid.NewGuid();
        VincularA(empresaId);
        var perfis = Substitute.For<IUsuarioPerfilRepository>();
        perfis.ListarPorUsuarioEEmpresaAsync(_usuario.Id, empresaId).Returns([]);

        await AtribuirPerfil(perfis, empresaId)
            .ExecuteAsync(new AtribuirPerfilUsuarioCommand(_usuario.Id, empresaId, perfilId, LojaId: null));

        await perfis.Received(1).AddAsync(Arg.Is<UsuarioPerfil>(p => p.PerfilId == perfilId));
        await DeveTerRevogadoOUsuarioTodo();
    }

    [Fact]
    public async Task ReatribuirOMesmoPerfilTambemRevoga()
    {
        var empresaId = Guid.NewGuid();
        var perfilId = Guid.NewGuid();
        VincularA(empresaId);
        var existente = new UsuarioPerfil { Id = Guid.NewGuid(), UsuarioId = _usuario.Id, EmpresaId = empresaId, PerfilId = perfilId, AtribuidoEm = DateTime.UtcNow.AddDays(-3) };
        var perfis = Substitute.For<IUsuarioPerfilRepository>();
        perfis.ListarPorUsuarioEEmpresaAsync(_usuario.Id, empresaId).Returns([existente]);

        await AtribuirPerfil(perfis, empresaId)
            .ExecuteAsync(new AtribuirPerfilUsuarioCommand(_usuario.Id, empresaId, perfilId, LojaId: Guid.NewGuid()));

        await perfis.Received(1).UpdateAsync(existente);
        await DeveTerRevogadoOUsuarioTodo();
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, "USUARIO_DESATIVADO")]
    public async Task PerfilEmEmpresaOndeOUsuarioNaoEstaAtivoERecusadoSemDerrubarASessao(bool temVinculoInativo, string? codigo)
    {
        // O corte é do usuário todo, e um Admin de outra empresa não pode usar esta rota para deslogar quem não é dele.
        // #2070 (B3): a atribuição exige vínculo ativo com a empresa; sem ele nada muda (antes atribuía e só não revogava).
        var empresaDoAdmin = Guid.NewGuid();
        var perfilId = Guid.NewGuid();
        VincularA(Guid.NewGuid()); // vínculo ativo, mas com outra empresa
        if (temVinculoInativo) VincularA(empresaDoAdmin, ativo: false);
        var perfis = Substitute.For<IUsuarioPerfilRepository>();
        perfis.ListarPorUsuarioEEmpresaAsync(_usuario.Id, empresaDoAdmin).Returns([]);

        var atribuir = () => AtribuirPerfil(perfis, empresaDoAdmin)
            .ExecuteAsync(new AtribuirPerfilUsuarioCommand(_usuario.Id, empresaDoAdmin, perfilId, LojaId: null));

        var recusa = (await atribuir.Should().ThrowAsync<UseCaseValidationException>()).Which;
        recusa.Code.Should().Be(codigo);
        if (codigo is null) recusa.Message.Should().Be("Usuario nao encontrado.");
        await perfis.DidNotReceiveWithAnyArgs().AddAsync(default!);
        await NaoDeveTerRevogadoNada();
    }

    // ── troca de contato (N4): só o que muda a identidade da conta derruba a sessão ──────────

    private TrocaDeContatoFixture Contato()
    {
        var f = new TrocaDeContatoFixture(usuarios: _usuarios, unitOfWork: _unitOfWork);
        f.UsuarioAtual.UsuarioId.Returns(_usuario.Id);
        return f;
    }

    [Fact]
    public async Task ConfirmarNovoEmailRevoga()
    {
        _usuario.SolicitarTrocaDeEmail("nova@casadababa.com");
        var tokens = Substitute.For<IEmailConfirmationTokenRepository>();
        tokens.GetByTokenAsync("token-do-email").Returns(EmailConfirmationToken.Criar(_usuario.Id, "hash", null, null));

        await new ConfirmEmailUseCase(
                tokens, _usuarios, Substitute.For<IAuditLogRepository>(), _revogador, _unitOfWork,
                Substitute.For<ILogger<ConfirmEmailUseCase>>())
            .ExecuteAsync(new ConfirmEmailCommand("token-do-email"));

        _usuario.Email.Should().Be("nova@casadababa.com");
        await DeveTerRevogadoOUsuarioTodo();
    }

    [Fact]
    public async Task PedidoDeTrocaDeEmailNaoRevoga()
    {
        var f = Contato();

        await new AtualizarUsuarioAtualUseCase(
                _usuarios, f.UsuarioAtual, _unitOfWork, f.Servico, Substitute.For<ILogger<AtualizarUsuarioAtualUseCase>>())
            .ExecuteAsync(new AtualizarUsuarioAtualCommand(null, "nova@casadababa.com", SenhaAtual: "SenhaAntiga@123"));

        _usuario.EmailPendente.Should().Be("nova@casadababa.com");
        await NaoDeveTerRevogadoNada();
    }

    [Fact]
    public async Task TrocaDeTelefoneRevoga()
    {
        var f = Contato();
        _usuario.DefinirTelefone(TelefoneE164.From("11997573992"));

        await new DefinirMeuTelefoneUseCase(
                _usuarios, f.UsuarioAtual, f.Servico, _revogador, _unitOfWork, Substitute.For<ILogger<DefinirMeuTelefoneUseCase>>())
            .ExecuteAsync(new DefinirMeuTelefoneCommand("11988887777", "SenhaAntiga@123"));

        _usuario.Telefone!.Value.Should().Be("+5511988887777");
        await DeveTerRevogadoOUsuarioTodo();
    }

    // ── login: nunca revoga ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoginNaoRevoga()
    {
        // Logar em outro aparelho revoga só o refresh dos outros (AuthController); o JWT deles segue valendo,
        // então o carimbo não pode mudar no login.
        VincularA(Guid.NewGuid()); // sem vínculo ativo o login é recusado (#1540)
        _usuarios.GetByEmailAsync(_usuario.Email).Returns(_usuario);
        _usuarios.ReservarTentativaDeSenhaAsync(_usuario.Id, Arg.Any<DateTime>()).Returns(true);
        var login = new AutenticarUsuarioUseCase(
            _usuarios, _unitOfWork, new FakePasswordHasher(), Substitute.For<ILogger<AutenticarUsuarioUseCase>>());

        await login.ExecuteAsync(new AutenticarUsuarioCommand(_usuario.Email, "SenhaAntiga@123", null));

        await NaoDeveTerRevogadoNada();
    }
}

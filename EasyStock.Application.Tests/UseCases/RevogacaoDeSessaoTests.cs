using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Auth;
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

    private (ResetarSenhaUseCase UseCase, ResetToken Token, IAuditLogRepository Auditoria) ResetarSenha(bool tokenValido = true)
    {
        var resetTokens = Substitute.For<IResetTokenRepository>();
        var token = ResetToken.Criar(_usuario.Id, "hash-do-token", DateTime.UtcNow.AddMinutes(30), null, null);
        resetTokens.GetByTokenAsync("token-do-email").Returns(tokenValido ? token : null);
        var auditoria = Substitute.For<IAuditLogRepository>();
        var useCase = new ResetarSenhaUseCase(
            resetTokens, _usuarios, auditoria, _revogador, _unitOfWork,
            new FakePasswordHasher(), Substitute.For<ILogger<ResetarSenhaUseCase>>());
        return (useCase, token, auditoria);
    }

    [Fact]
    public async Task ResetDeSenhaRevogaAsSessoes()
    {
        _refreshTokens.RevogarSessoesAtivasAsync(_usuario.Id, Agora.UtcDateTime).Returns(2);
        var (useCase, token, auditoria) = ResetarSenha();

        await useCase.ExecuteAsync(new ResetarSenhaCommand("token-do-email", "NovaSenha@123"));

        await DeveTerRevogadoOUsuarioTodo();
        _usuario.SenhaHash.Should().Be(FakePasswordHasher.MakeHash("NovaSenha@123"));
        token.Usado.Should().BeTrue();
        await auditoria.Received(1).AddAsync(Arg.Is<AuditLog>(a => a.Detalhes!.Contains("2 refresh tokens revogados")));
        // O laço N+1 saiu de cena: nenhum refresh token é lido nem atualizado um a um.
        await _refreshTokens.DidNotReceiveWithAnyArgs().GetByUsuarioIdAsync(default);
        await _refreshTokens.DidNotReceiveWithAnyArgs().UpdateAsync(default!);
    }

    [Fact]
    public async Task ResetComTokenInvalidoNaoRevoga()
    {
        var (useCase, _, _) = ResetarSenha(tokenValido: false);

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
        var vinculos = Substitute.For<IUsuarioEmpresaRepository>();
        vinculos.GetByUsuarioEEmpresaAsync(_usuario.Id, empresaId).Returns(vinculo);

        await new DesativarUsuarioUseCase(
                _usuarios, vinculos, _revogador, _unitOfWork, Substitute.For<ILogger<DesativarUsuarioUseCase>>())
            .ExecuteAsync(new DesativarUsuarioCommand(_usuario.Id, empresaId));

        vinculo.Ativo.Should().BeFalse();
        await DeveTerRevogadoOUsuarioTodo();
    }

    [Fact]
    public async Task DesativarSemVinculoNaoRevoga()
    {
        var vinculos = Substitute.For<IUsuarioEmpresaRepository>();

        await Assert.ThrowsAsync<UseCaseValidationException>(() => new DesativarUsuarioUseCase(
                _usuarios, vinculos, _revogador, _unitOfWork, Substitute.For<ILogger<DesativarUsuarioUseCase>>())
            .ExecuteAsync(new DesativarUsuarioCommand(_usuario.Id, Guid.NewGuid())));

        await NaoDeveTerRevogadoNada();
    }

    // ── troca de perfil ───────────────────────────────────────────────────────────────────────

    private void VincularA(Guid empresaId, bool ativo = true) =>
        _usuario.Empresas.Add(new UsuarioEmpresa { Id = Guid.NewGuid(), UsuarioId = _usuario.Id, EmpresaId = empresaId, Ativo = ativo, CriadoEm = DateTime.UtcNow });

    private AtribuirPerfilUsuarioUseCase AtribuirPerfil(IUsuarioPerfilRepository perfis) =>
        new(_usuarios, perfis, _revogador, _unitOfWork, Substitute.For<ILogger<AtribuirPerfilUsuarioUseCase>>());

    [Fact]
    public async Task TrocaDePerfilRevoga()
    {
        var empresaId = Guid.NewGuid();
        var perfilId = Guid.NewGuid();
        VincularA(empresaId);
        var perfis = Substitute.For<IUsuarioPerfilRepository>();
        perfis.GetByUsuarioEmpresaEPerfilAsync(_usuario.Id, empresaId, perfilId).Returns((UsuarioPerfil?)null);

        await AtribuirPerfil(perfis)
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
        perfis.GetByUsuarioEmpresaEPerfilAsync(_usuario.Id, empresaId, perfilId).Returns(existente);

        await AtribuirPerfil(perfis)
            .ExecuteAsync(new AtribuirPerfilUsuarioCommand(_usuario.Id, empresaId, perfilId, LojaId: Guid.NewGuid()));

        await perfis.Received(1).UpdateAsync(existente);
        await DeveTerRevogadoOUsuarioTodo();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PerfilEmEmpresaOndeOUsuarioNaoEstaAtivoNaoDerrubaASessao(bool temVinculoInativo)
    {
        // O corte é do usuário todo, e esta rota só confere a empresa do chamador: um Admin de outra empresa
        // não pode usá-la para deslogar quem não é dele. A atribuição segue como sempre; só não revoga.
        var empresaDoAdmin = Guid.NewGuid();
        var perfilId = Guid.NewGuid();
        VincularA(Guid.NewGuid()); // vínculo ativo, mas com outra empresa
        if (temVinculoInativo) VincularA(empresaDoAdmin, ativo: false);
        var perfis = Substitute.For<IUsuarioPerfilRepository>();

        await AtribuirPerfil(perfis)
            .ExecuteAsync(new AtribuirPerfilUsuarioCommand(_usuario.Id, empresaDoAdmin, perfilId, LojaId: null));

        await perfis.Received(1).AddAsync(Arg.Is<UsuarioPerfil>(p => p.PerfilId == perfilId));
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
        _usuarios.GetByEmailAsync(_usuario.Email).Returns(_usuario);
        var login = new AutenticarUsuarioUseCase(
            _usuarios, _unitOfWork, new FakePasswordHasher(), Substitute.For<ILogger<AutenticarUsuarioUseCase>>());

        await login.ExecuteAsync(new AutenticarUsuarioCommand(_usuario.Email, "SenhaAntiga@123", null));

        await NaoDeveTerRevogadoNada();
    }
}

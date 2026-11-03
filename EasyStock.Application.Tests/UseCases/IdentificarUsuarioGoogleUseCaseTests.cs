using EasyStock.Application.Ports.Output.Auth;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Auth;
using EasyStock.Application.Tests.Services.Auth;
using EasyStock.Application.UseCases.AutenticarUsuario;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>
/// #1324: login com Google acha o usuário que já existe pelo e-mail do Google. Ninguém é criado; o alias do
/// Gmail (<c>nome+x@gmail.com</c>) casa com <c>nome@gmail.com</c> quando é uma conta só.
/// </summary>
public class IdentificarUsuarioGoogleUseCaseTests
{
    private const string Token = "id-token";
    private readonly IGoogleIdTokenValidator _validador = Substitute.For<IGoogleIdTokenValidator>();
    private readonly IUsuarioRepository _usuarios = Substitute.For<IUsuarioRepository>();

    private readonly CenarioDeAcesso _c = new();

    private IdentificarUsuarioGoogleUseCase UseCase() => new(_validador, _usuarios, _c.Convites(), _c.UnitOfWork, _c.Relogio);

    private static Usuario Usuario(string email, bool ativo = true) => new()
    {
        Id = Guid.NewGuid(), Nome = "Felipe", Email = email, SenhaHash = "x", Ativo = ativo,
        CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow
    };

    private void Google(string email, bool verificado = true) =>
        _validador.ValidarAsync(Token, Arg.Any<CancellationToken>()).Returns(new IdentidadeGoogle(email, verificado));

    [Fact]
    public async Task AchaPeloEmailExato()
    {
        var usuario = Usuario("dona@casadababa.com.br");
        Google("Dona@CasaDaBaba.com.br");
        _usuarios.GetByEmailAsync("dona@casadababa.com.br").Returns(usuario);

        (await UseCase().ExecuteAsync(Token)).Should().BeSameAs(usuario);
    }

    [Fact]
    public async Task AliasDoGmailComUmaContaEntra()
    {
        var usuario = Usuario("felipe.azevedoit+demo@gmail.com");
        Google("felipe.azevedoit@gmail.com");
        _usuarios.ListarPorAliasGmailAsync("felipe.azevedoit").Returns([usuario]);

        (await UseCase().ExecuteAsync(Token)).Should().BeSameAs(usuario);
    }

    [Fact]
    public async Task AliasDoGmailComVariasContasRecusa()
    {
        Google("felipe.azevedoit@gmail.com");
        _usuarios.ListarPorAliasGmailAsync("felipe.azevedoit")
            .Returns([Usuario("felipe.azevedoit+a@gmail.com"), Usuario("felipe.azevedoit+b@gmail.com")]);

        await UseCase().Invoking(u => u.ExecuteAsync(Token)).Should().ThrowAsync<CredenciaisInvalidasException>();
    }

    [Fact]
    public async Task AliasSoValeParaGmail()
    {
        Google("felipe@empresa.com");

        await UseCase().Invoking(u => u.ExecuteAsync(Token)).Should().ThrowAsync<CredenciaisInvalidasException>();
        await _usuarios.DidNotReceive().ListarPorAliasGmailAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task TokenInvalidoRecusa()
    {
        _validador.ValidarAsync(Token, Arg.Any<CancellationToken>()).Returns((IdentidadeGoogle?)null);

        await UseCase().Invoking(u => u.ExecuteAsync(Token)).Should().ThrowAsync<CredenciaisInvalidasException>();
    }

    [Fact]
    public async Task EmailNaoVerificadoRecusa()
    {
        Google("dona@casadababa.com.br", verificado: false);
        _usuarios.GetByEmailAsync("dona@casadababa.com.br").Returns(Usuario("dona@casadababa.com.br"));

        await UseCase().Invoking(u => u.ExecuteAsync(Token)).Should().ThrowAsync<CredenciaisInvalidasException>();
    }

    [Fact]
    public async Task UsuarioInativoRecusa()
    {
        Google("dona@casadababa.com.br");
        _usuarios.GetByEmailAsync("dona@casadababa.com.br").Returns(Usuario("dona@casadababa.com.br", ativo: false));

        await UseCase().Invoking(u => u.ExecuteAsync(Token)).Should().ThrowAsync<CredenciaisInvalidasException>();
    }

    [Fact]
    public async Task SemUsuarioNaoCriaConta()
    {
        Google("novo@gmail.com");
        _usuarios.ListarPorAliasGmailAsync("novo").Returns([]);

        await UseCase().Invoking(u => u.ExecuteAsync(Token)).Should().ThrowAsync<CredenciaisInvalidasException>();
        await _usuarios.DidNotReceive().AddAsync(Arg.Any<Usuario>());
    }
    // ── N9: o Google prova o e-mail, então consome o convite ────────────────────────────────────────────────

    [Fact]
    public async Task GoogleDeConvidadoConsomeOConviteEConfirmaOEmail()
    {
        var convidado = Domain.Entities.Usuario.CriarConvidado("Ana", "ana@casadababa.com.br");
        await _c.Convites().EmitirAsync(convidado, _c.EmpresaPadraoId, false, null, null);
        Google("ana@casadababa.com.br");
        _usuarios.GetByEmailAsync("ana@casadababa.com.br").Returns(convidado);

        var usuario = await UseCase().ExecuteAsync(Token);

        usuario.Should().BeSameAs(convidado);
        convidado.EmailConfirmado.Should().BeTrue();
        convidado.ConvitePendente.Should().BeFalse();
        convidado.ConviteAceitoVia.Should().Be("Google");
        convidado.ConviteAceitoEm.Should().Be(_c.AgoraUtc);
        convidado.SenhaHash.Should().NotStartWith(Domain.Entities.Usuario.MarcadorDeConvite, "o esqueci a senha passa a valer");
        _c.Tokens.Linhas.Should().OnlyContain(l => l.Usado, "os convites abertos morrem");
        await _usuarios.Received(1).UpdateAsync(convidado);
        _c.UnitOfWork.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task GoogleDeConvidadoComEmailNaoVerificadoNaoConsomeOConvite()
    {
        var convidado = Domain.Entities.Usuario.CriarConvidado("Ana", "ana@casadababa.com.br");
        Google("ana@casadababa.com.br", verificado: false);
        _usuarios.GetByEmailAsync("ana@casadababa.com.br").Returns(convidado);

        await UseCase().Invoking(u => u.ExecuteAsync(Token)).Should().ThrowAsync<CredenciaisInvalidasException>();

        convidado.ConvitePendente.Should().BeTrue();
        convidado.EmailConfirmado.Should().BeFalse();
        _c.UnitOfWork.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task GoogleDeQuemNaoEConvidadoNaoMudaNada()
    {
        var existente = Usuario("dona@casadababa.com.br");
        Google("dona@casadababa.com.br");
        _usuarios.GetByEmailAsync("dona@casadababa.com.br").Returns(existente);

        await UseCase().ExecuteAsync(Token);

        existente.EmailConfirmado.Should().BeFalse("a regra do login Google não muda para quem já tem acesso");
        existente.ConviteAceitoEm.Should().BeNull();
        existente.ConviteAceitoVia.Should().BeNull();
        await _usuarios.DidNotReceive().UpdateAsync(Arg.Any<Usuario>());
        _c.UnitOfWork.CommitCount.Should().Be(0);
    }

    // ── #2070 (A3, B4): só convite aberto vale; senha errada não barra o Google ─────────────────────────────

    [Fact]
    public async Task ConviteVencidoNaoEConsumidoEPedeUmNovo()
    {
        var convidado = Domain.Entities.Usuario.CriarConvidado("Ana", "ana@exemplo.test");
        await _c.Convites().EmitirAsync(convidado, _c.EmpresaPadraoId, false, null, null);
        _c.Relogio.Advance(ConvitesDeAcesso.Validade + TimeSpan.FromMinutes(1));
        Google("ana@exemplo.test");
        _usuarios.GetByEmailAsync("ana@exemplo.test").Returns(convidado);

        var entrar = () => UseCase().ExecuteAsync(Token);

        (await entrar.Should().ThrowAsync<CredenciaisInvalidasException>()).Which.Message
            .Should().Be("Seu convite venceu: peça um novo.");
        convidado.ConvitePendente.Should().BeTrue();
        convidado.EmailConfirmado.Should().BeFalse();
        _c.UnitOfWork.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task ConviteRevogadoNaoEConsumido()
    {
        var convidado = Domain.Entities.Usuario.CriarConvidado("Ana", "ana@exemplo.test");
        await _c.Convites().EmitirAsync(convidado, _c.EmpresaPadraoId, false, null, null);
        await _c.Convites().RevogarAbertosAsync(convidado.Id);
        Google("ana@exemplo.test");
        _usuarios.GetByEmailAsync("ana@exemplo.test").Returns(convidado);

        var entrar = () => UseCase().ExecuteAsync(Token);

        (await entrar.Should().ThrowAsync<CredenciaisInvalidasException>()).Which.Message
            .Should().Be("Seu convite venceu: peça um novo.");
        convidado.ConvitePendente.Should().BeTrue();
    }

    [Fact]
    public async Task ConvidadoSemConviteNenhumNaoEntra()
    {
        var convidado = Domain.Entities.Usuario.CriarConvidado("Ana", "ana@exemplo.test");
        Google("ana@exemplo.test");
        _usuarios.GetByEmailAsync("ana@exemplo.test").Returns(convidado);

        await UseCase().Invoking(u => u.ExecuteAsync(Token)).Should().ThrowAsync<CredenciaisInvalidasException>();
        convidado.ConvitePendente.Should().BeTrue();
    }

    [Fact]
    public async Task BloqueioPorSenhaErradaNaoBarraOGoogle()
    {
        var existente = Usuario("dona@exemplo.test");
        for (var i = 0; i < Domain.Entities.Usuario.FalhasParaBloquear; i++) existente.RegistrarFalhaDeSenha();
        existente.EstaBloqueado().Should().BeTrue("cinco senhas erradas bloqueiam o login por senha");
        Google("dona@exemplo.test");
        _usuarios.GetByEmailAsync("dona@exemplo.test").Returns(existente);

        (await UseCase().ExecuteAsync(Token)).Should().BeSameAs(existente);
    }

    [Fact]
    public async Task ConvidadoBloqueadoPorSenhaComConviteAbertoEntraEConsome()
    {
        var convidado = Domain.Entities.Usuario.CriarConvidado("Ana", "ana@exemplo.test");
        await _c.Convites().EmitirAsync(convidado, _c.EmpresaPadraoId, false, null, null);
        convidado.BloquearPorTentativas();
        Google("ana@exemplo.test");
        _usuarios.GetByEmailAsync("ana@exemplo.test").Returns(convidado);

        await UseCase().ExecuteAsync(Token);

        convidado.ConvitePendente.Should().BeFalse();
        convidado.ConviteAceitoVia.Should().Be("Google");
    }
}

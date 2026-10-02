using EasyStock.Application.Ports.Output.Auth;
using EasyStock.Application.Ports.Output.Persistence;
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

    private IdentificarUsuarioGoogleUseCase UseCase() => new(_validador, _usuarios);

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
}

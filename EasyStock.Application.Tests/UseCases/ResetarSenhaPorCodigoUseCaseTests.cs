using EasyStock.Application.Tests.Services.Auth;
using EasyStock.Application.UseCases.ResetarSenha;
using EasyStock.TestHelpers;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>N8: o código de 6 dígitos do WhatsApp, 10 min, 5 tentativas, uso único, nunca para superadmin.</summary>
public class ResetarSenhaPorCodigoUseCaseTests
{
    private const string NovaSenha = "Nova@Senha123";
    private readonly CenarioDeAcesso _c = new();

    private Task<ResetarSenhaResult> Confirmar(string email, string codigo, string senha = NovaSenha, string ip = "198.51.100.30") =>
        _c.ResetarSenhaPorCodigo().ExecuteAsync(new ResetarSenhaPorCodigoCommand(email, codigo, senha, ip, "App/1.0"));

    private static string Errado(string certo) => certo == "000000" ? "111111" : "000000";

    [Fact]
    public async Task CodigoCertoTrocaASenha()
    {
        var usuario = _c.CriarUsuarioElegivelAoCodigo();
        var (token, codigo) = await _c.PedirAsync(usuario);

        var resultado = await Confirmar(usuario.Email, codigo!);

        resultado.Success.Should().BeTrue();
        usuario.SenhaHash.Should().Be(FakePasswordHasher.MakeHash(NovaSenha));
        usuario.SessoesValidasDesde.Should().NotBeNull("revoga as sessões como o reset por link");
        _c.Tokens.Linhas.Should().OnlyContain(l => l.Usado, "usar o código mata o link");
        var linkDepois = () => _c.ResetarSenha().ExecuteAsync(new ResetarSenhaCommand(token, "Outra@Senha456"));
        await linkDepois.Should().ThrowAsync<RegraDeDominioVioladaException>();
        _c.Auditorias.Should().Contain(a => a.Acao == "reset-password-code" && a.Ip == "198.51.100.30");
    }

    [Fact]
    public async Task UsarOLinkMataOCodigo()
    {
        var usuario = _c.CriarUsuarioElegivelAoCodigo();
        var (token, codigo) = await _c.PedirAsync(usuario);
        await _c.ResetarSenha().ExecuteAsync(new ResetarSenhaCommand(token, NovaSenha));

        var act = () => Confirmar(usuario.Email, codigo!, "Outra@Senha456");

        await act.Should().ThrowAsync<RegraDeDominioVioladaException>();
    }

    [Fact]
    public async Task CincoErradosMatamOToken()
    {
        var usuario = _c.CriarUsuarioElegivelAoCodigo();
        var (_, codigo) = await _c.PedirAsync(usuario);

        for (var i = 0; i < 5; i++)
        {
            var errado = () => Confirmar(usuario.Email, Errado(codigo!));
            await errado.Should().ThrowAsync<RegraDeDominioVioladaException>();
        }

        var linha = _c.Tokens.Linhas.Single(l => l.Finalidade == FinalidadeResetToken.ResetCodigo);
        linha.Tentativas.Should().Be(5);
        usuario.SenhaHash.Should().Be(FakePasswordHasher.MakeHash("Senha@12345"));
    }

    [Fact]
    public async Task SextaTentativaComCodigoCertoFalha()
    {
        var usuario = _c.CriarUsuarioElegivelAoCodigo();
        var (_, codigo) = await _c.PedirAsync(usuario);
        for (var i = 0; i < 5; i++)
            await FluentActions.Awaiting(() => Confirmar(usuario.Email, Errado(codigo!))).Should().ThrowAsync<RegraDeDominioVioladaException>();

        var certo = () => Confirmar(usuario.Email, codigo!, ip: "198.51.100.31"); // outro IP: o teto de IP não mascara o do código

        await certo.Should().ThrowAsync<RegraDeDominioVioladaException>("depois de 5 erros o código morreu, mesmo certo");
        usuario.SenhaHash.Should().Be(FakePasswordHasher.MakeHash("Senha@12345"));
    }

    [Fact]
    public async Task CodigoExpiradoFalha()
    {
        var usuario = _c.CriarUsuarioElegivelAoCodigo();
        var (_, codigo) = await _c.PedirAsync(usuario);
        _c.Relogio.Advance(TimeSpan.FromMinutes(11));

        var act = () => Confirmar(usuario.Email, codigo!);

        await act.Should().ThrowAsync<RegraDeDominioVioladaException>();
    }

    [Fact]
    public async Task SuperadminNaoRedefinePorCodigo()
    {
        var usuario = _c.CriarUsuarioElegivelAoCodigo();
        var (_, codigo) = await _c.PedirAsync(usuario);
        CenarioDeAcesso.TornarSuperAdmin(usuario);

        var act = () => Confirmar(usuario.Email, codigo!);

        var erro = (await act.Should().ThrowAsync<RegraDeDominioVioladaException>()).Which;
        erro.Message.Should().Be(await MensagemDeCodigoInexistente(), "a mesma mensagem genérica de qualquer recusa");
        usuario.SenhaHash.Should().Be(FakePasswordHasher.MakeHash("Senha@12345"));
    }

    [Fact]
    public async Task CodigoDeOutraContaNaoServe()
    {
        var ana = _c.CriarUsuarioElegivelAoCodigo("ana@casadababa.com");
        var bia = _c.CriarUsuarioElegivelAoCodigo("bia@casadababa.com");
        var (_, codigoDaAna) = await _c.PedirAsync(ana);
        _c.Relogio.Advance(TimeSpan.FromMinutes(2));
        await _c.PedirAsync(bia);

        var act = () => Confirmar(bia.Email, codigoDaAna!);

        await act.Should().ThrowAsync<RegraDeDominioVioladaException>();
        bia.SenhaHash.Should().Be(FakePasswordHasher.MakeHash("Senha@12345"));
        ana.SenhaHash.Should().Be(FakePasswordHasher.MakeHash("Senha@12345"));
    }

    [Fact]
    public async Task ContaInexistenteRecebeAMesmaRecusa()
    {
        _c.Usuarios.GetByEmailAsync("ninguem@casadababa.com").Returns((Usuario?)null);

        var act = () => Confirmar("ninguem@casadababa.com", "123456");

        (await act.Should().ThrowAsync<RegraDeDominioVioladaException>()).Which.Message.Should().Be(await MensagemDeCodigoInexistente());
    }

    [Fact]
    public async Task CodigoComFormatoInvalidoNaoGastaTentativa()
    {
        var usuario = _c.CriarUsuarioElegivelAoCodigo();
        await _c.PedirAsync(usuario);

        var act = () => Confirmar(usuario.Email, "12ab");

        await act.Should().ThrowAsync<RegraDeDominioVioladaException>();
        _c.Tokens.Linhas.Single(l => l.Finalidade == FinalidadeResetToken.ResetCodigo).Tentativas.Should().Be(0);
    }

    [Fact]
    public async Task SenhaForaDaPoliticaRecusaSemGastarOCodigo()
    {
        var usuario = _c.CriarUsuarioElegivelAoCodigo();
        var (_, codigo) = await _c.PedirAsync(usuario);

        var act = () => Confirmar(usuario.Email, codigo!, senha: "fraca");

        await act.Should().ThrowAsync<UseCaseValidationException>();
        _c.Tokens.Linhas.Single(l => l.Finalidade == FinalidadeResetToken.ResetCodigo).Usado.Should().BeFalse();
    }

    private async Task<string> MensagemDeCodigoInexistente()
    {
        var fantasma = _c.CriarUsuario("fantasma@casadababa.com");
        try
        {
            await Confirmar(fantasma.Email, "123456");
        }
        catch (RegraDeDominioVioladaException ex)
        {
            return ex.Message;
        }
        throw new InvalidOperationException("Esperava recusa.");
    }
}

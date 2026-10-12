using EasyStock.Application.Services.Auth;
using EasyStock.Application.Tests.Services.Auth;
using EasyStock.Application.UseCases.ResetarSenha;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.TestHelpers;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>N8: o link é de uso único de verdade (UPDATE condicional), revoga as sessões e avisa os canais verificados.</summary>
public class ResetarSenhaUseCaseTests
{
    private const string NovaSenha = "Nova@Senha123";
    private readonly CenarioDeAcesso _c = new();

    private Task<ResetarSenhaResult> Resetar(string token, string senha = NovaSenha, string? ip = "198.51.100.20") =>
        _c.ResetarSenha().ExecuteAsync(new ResetarSenhaCommand(token, senha, ip, "Navegador/2.0"));

    [Fact]
    public async Task UsoUnicoUmSoPassa()
    {
        var usuario = _c.CriarUsuario();
        var (token, _) = await _c.PedirAsync(usuario);

        var primeiro = await Resetar(token);
        var segundo = () => Resetar(token);

        primeiro.Success.Should().BeTrue();
        await segundo.Should().ThrowAsync<RegraDeDominioVioladaException>("o fake devolve 0 linhas ao segundo UPDATE");
        usuario.SenhaHash.Should().Be(FakePasswordHasher.MakeHash(NovaSenha));
    }

    [Fact]
    public async Task ConsumeAntesDeTrocarASenha()
    {
        // Dois POST simultâneos: quem perde a corrida do UPDATE não pode trocar a senha.
        var usuario = _c.CriarUsuario();
        var (token, _) = await _c.PedirAsync(usuario);
        _c.Tokens.PerderACorridaDoConsumo = true; // a leitura do perdedor ainda via o token aberto

        var act = () => Resetar(token);

        await act.Should().ThrowAsync<RegraDeDominioVioladaException>();
        usuario.SenhaHash.Should().Be(FakePasswordHasher.MakeHash("Senha@12345"), "a senha não troca sem o UPDATE valer");
    }

    [Fact]
    public async Task TokenDeOutraFinalidadeNaoServe()
    {
        var usuario = _c.CriarUsuarioElegivelAoCodigo();
        var (_, codigo) = await _c.PedirAsync(usuario);
        var tokenDoCodigo = _c.Tokens.Linhas.Single(l => l.Finalidade == FinalidadeResetToken.ResetCodigo);

        // Um segredo com a finalidade errada não passa nem se o atacante souber o texto que gerou o hash.
        var forjado = ResetToken.Criar(usuario.Id, TokenHashHelper.ComputeSha256Hash("texto-do-codigo"),
            _c.AgoraUtc.AddMinutes(10), null, null, FinalidadeResetToken.ResetCodigo);
        await _c.Tokens.AddAsync(forjado);

        var act = () => Resetar("texto-do-codigo");

        await act.Should().ThrowAsync<RegraDeDominioVioladaException>();
        codigo.Should().NotBeNull();
        tokenDoCodigo.Usado.Should().BeFalse();
        forjado.Usado.Should().BeFalse();
    }

    [Fact]
    public async Task TokenExpiradoNaoServe()
    {
        var usuario = _c.CriarUsuario();
        var (token, _) = await _c.PedirAsync(usuario);
        _c.Relogio.Advance(TimeSpan.FromMinutes(31));

        var act = () => Resetar(token);

        await act.Should().ThrowAsync<RegraDeDominioVioladaException>();
    }

    [Fact]
    public async Task RevogaSessoesPeloRevogador()
    {
        var usuario = _c.CriarUsuario();
        var (token, _) = await _c.PedirAsync(usuario);

        await Resetar(token);

        usuario.SessoesValidasDesde.Should().NotBeNull();
        await _c.Usuarios.Received(1).AtualizarSessoesValidasDesdeAsync(usuario.Id, usuario.SessoesValidasDesde!.Value);
        await _c.RefreshTokens.Received(1).RevogarSessoesAtivasAsync(usuario.Id, Arg.Any<DateTime>());
        await _c.CacheDoRevogador.Received(1).RemoveAsync(CacheKeys.Sessao(usuario.Id));
    }

    [Fact]
    public async Task InvalidaOsDemaisSegredosEZeraOContadorDeFalhas()
    {
        var usuario = _c.CriarUsuarioElegivelAoCodigo();
        usuario.RegistrarFalhaDeSenha();
        await _c.PedirAsync(usuario);
        _c.Relogio.Advance(TimeSpan.FromMinutes(2));
        var (token, _) = await _c.PedirAsync(usuario);
        _c.Tokens.Linhas.Where(l => !l.Usado).Should().NotBeEmpty();

        await Resetar(token);

        _c.Tokens.Linhas.Should().OnlyContain(l => l.Usado, "um reset concluído mata o link e o código");
        usuario.FailedLoginAttempts.Should().Be(0);
    }

    [Fact]
    public async Task AvisaOsCanaisVerificados()
    {
        var usuario = _c.CriarUsuario();
        var (token, _) = await _c.PedirAsync(usuario);
        _c.Eventos.Clear();

        await Resetar(token);

        var evento = _c.Eventos.Should().ContainSingle().Subject;
        evento.Tipo.Should().Be(TipoEventoNotificacao.SenhaAlterada);
        evento.Payload.GetProperty("usuarioId").GetGuid().Should().Be(usuario.Id);
        evento.Payload.GetProperty("data").GetString().Should().MatchRegex(@"^\d{2}/\d{2}/\d{4} \d{2}:\d{2}$");
        evento.Payload.TryGetProperty("canais", out _).Should().BeFalse("o aviso vai por todos os canais verificados");
    }

    [Fact]
    public async Task GravaIpEAgenteNaAuditoriaDoReset()
    {
        var usuario = _c.CriarUsuario();
        var (token, _) = await _c.PedirAsync(usuario);

        await Resetar(token, ip: "198.51.100.20");

        var auditoria = _c.Auditorias.Should().ContainSingle(a => a.Acao == "reset-password").Subject;
        auditoria.Ip.Should().Be("198.51.100.20");
        auditoria.UserAgent.Should().Be("Navegador/2.0");
    }

    [Fact]
    public async Task SenhaForaDaPoliticaRecusa()
    {
        var usuario = _c.CriarUsuario();
        var (token, _) = await _c.PedirAsync(usuario);

        var act = () => Resetar(token, senha: "fraca");

        await act.Should().ThrowAsync<UseCaseValidationException>();
        (await _c.Tokens.GetByTokenAsync(token))!.Usado.Should().BeFalse("senha ruim não gasta o link");
        usuario.SenhaHash.Should().Be(FakePasswordHasher.MakeHash("Senha@12345"));
    }

    [Fact]
    public async Task UsuarioInativoNaoRedefine()
    {
        var usuario = _c.CriarUsuario();
        var (token, _) = await _c.PedirAsync(usuario);
        usuario.Ativo = false;

        var act = () => Resetar(token);

        await act.Should().ThrowAsync<RegraDeDominioVioladaException>();
    }

    [Fact]
    public async Task OLimiteDeIpTambemValeParaOReset()
    {
        var usuario = _c.CriarUsuario();
        var (token, _) = await _c.PedirAsync(usuario);
        for (var i = 0; i < 5; i++)
            await _c.Limite().TentarAsync("198.51.100.20");

        var act = () => Resetar(token, ip: "198.51.100.20");

        await act.Should().ThrowAsync<LimitePedidosAcessoExcedidoException>("o IP já gastou as 5 tentativas da janela");
    }
}

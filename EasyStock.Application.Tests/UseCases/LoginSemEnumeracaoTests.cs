using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.AutenticarUsuario;
using EasyStock.TestHelpers;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>
/// #1781: o login (passo 1 lista-empresas e login completo) não pode distinguir conta inexistente, inativa,
/// bloqueada e senha errada, nem pelo tempo (o BCrypt roda sempre) nem pela mensagem. A tentativa de senha
/// é reservada no banco, num UPDATE atômico, antes de conferir a senha: rajada paralela não fura o limite.
/// </summary>
public class LoginSemEnumeracaoTests
{
    private const string Email = "carlos@empresa.com";

    private static Usuario UsuarioDeSenha(string senha, bool ativo = true) => new()
    {
        Id = Guid.NewGuid(),
        Nome = "Carlos",
        Email = Email,
        SenhaHash = FakePasswordHasher.MakeHash(senha),
        Ativo = ativo,
        CriadoEm = DateTime.UtcNow,
        AlteradoEm = DateTime.UtcNow,
    };

    private static IPasswordHasher HasherQueDelegaAoFake()
    {
        var fake = new FakePasswordHasher();
        var hasher = Substitute.For<IPasswordHasher>();
        hasher.Verify(Arg.Any<string>(), Arg.Any<string>())
            .Returns(c => fake.Verify(c.ArgAt<string>(0), c.ArgAt<string>(1)));
        return hasher;
    }

    private static IUsuarioRepository RepoCom(Usuario? usuario, bool reservaAceita = true)
    {
        var repo = Substitute.For<IUsuarioRepository>();
        repo.GetByEmailAsync(Email).Returns(usuario);
        repo.ReservarTentativaDeSenhaAsync(Arg.Any<Guid>(), Arg.Any<DateTime>()).Returns(reservaAceita);
        return repo;
    }

    private static AutenticarUsuarioUseCase Login(IUsuarioRepository repo, IPasswordHasher hasher) =>
        new(repo, new FakeUnitOfWork(), hasher, Substitute.For<ILogger<AutenticarUsuarioUseCase>>());

    private static ListarEmpresasParaLoginUseCase Passo1(IUsuarioRepository repo, IPasswordHasher hasher) =>
        new(repo, hasher, Substitute.For<ILogger<ListarEmpresasParaLoginUseCase>>());

    private static Task<CredenciaisInvalidasException> LoginFalha(IUsuarioRepository repo, IPasswordHasher hasher, string senha) =>
        Assert.ThrowsAsync<CredenciaisInvalidasException>(
            () => Login(repo, hasher).ExecuteAsync(new AutenticarUsuarioCommand(Email, senha, null)));

    private static Task<CredenciaisInvalidasException> Passo1Falha(IUsuarioRepository repo, IPasswordHasher hasher, string senha) =>
        Assert.ThrowsAsync<CredenciaisInvalidasException>(
            () => Passo1(repo, hasher).ExecuteAsync(new ListarEmpresasParaLoginCommand(Email, senha)));

    private static readonly string MensagemGenerica = new CredenciaisInvalidasException().Message;

    // ── Tempo: o BCrypt roda contra o hash fictício quando não há conta a conferir ─────────────

    [Fact]
    public async Task LoginDeContaInexistenteRodaOBcryptContraOHashFicticio()
    {
        var hasher = HasherQueDelegaAoFake();

        await LoginFalha(RepoCom(null), hasher, "qualquer");

        hasher.Received(1).Verify("qualquer", SenhaFicticia.Hash);
    }

    [Fact]
    public async Task LoginDeContaInativaRodaOBcryptContraOHashFicticio()
    {
        var hasher = HasherQueDelegaAoFake();
        var usuario = UsuarioDeSenha("senhaCorreta", ativo: false);

        await LoginFalha(RepoCom(usuario), hasher, "senhaCorreta");

        hasher.Received(1).Verify("senhaCorreta", SenhaFicticia.Hash);
        hasher.DidNotReceive().Verify(Arg.Any<string>(), usuario.SenhaHash);
    }

    [Fact]
    public async Task Passo1DeContaInexistenteRodaOBcryptContraOHashFicticio()
    {
        var hasher = HasherQueDelegaAoFake();

        await Passo1Falha(RepoCom(null), hasher, "qualquer");

        hasher.Received(1).Verify("qualquer", SenhaFicticia.Hash);
    }

    [Fact]
    public async Task Passo1DeContaInativaRodaOBcryptContraOHashFicticio()
    {
        var hasher = HasherQueDelegaAoFake();
        var usuario = UsuarioDeSenha("senhaCorreta", ativo: false);

        await Passo1Falha(RepoCom(usuario), hasher, "senhaCorreta");

        hasher.Received(1).Verify("senhaCorreta", SenhaFicticia.Hash);
    }

    [Fact]
    public void HashFicticioEhBcryptDeCustoIgualAoDasSenhas()
    {
        // Mesmo custo (11) do BCryptPasswordHasher: custo diferente volta a separar os tempos.
        SenhaFicticia.Hash.Should().MatchRegex(@"^\$2a\$11\$[./A-Za-z0-9]{53}$");
    }

    // ── Mensagem: conta bloqueada responde como senha errada, sem conferir a senha real ─────────

    [Fact]
    public async Task LoginDeContaBloqueadaRespondeComoSenhaErrada()
    {
        var hasher = HasherQueDelegaAoFake();
        var usuario = UsuarioDeSenha("senhaCorreta");
        usuario.FailedLoginAttempts = 5;
        usuario.LockoutEnd = DateTime.UtcNow.AddMinutes(10);
        var repo = RepoCom(usuario);

        var recusa = await LoginFalha(repo, hasher, "senhaCorreta");

        recusa.Message.Should().Be(MensagemGenerica);
        hasher.Received(1).Verify("senhaCorreta", SenhaFicticia.Hash);
        hasher.DidNotReceive().Verify(Arg.Any<string>(), usuario.SenhaHash);
        await repo.DidNotReceiveWithAnyArgs().ReservarTentativaDeSenhaAsync(default, default);
    }

    [Fact]
    public async Task Passo1DeContaBloqueadaRespondeComoSenhaErrada()
    {
        var hasher = HasherQueDelegaAoFake();
        var usuario = UsuarioDeSenha("senhaCorreta");
        usuario.FailedLoginAttempts = 5;
        usuario.LockoutEnd = DateTime.UtcNow.AddMinutes(10);

        var recusa = await Passo1Falha(RepoCom(usuario), hasher, "senhaCorreta");

        recusa.Message.Should().Be(MensagemGenerica);
        hasher.DidNotReceive().Verify(Arg.Any<string>(), usuario.SenhaHash);
    }

    // ── Corrida: a tentativa é reservada no banco antes do BCrypt ─────────────────────────────

    [Fact]
    public async Task LoginReservaATentativaAntesDeConferirASenhaENaoRegravaOContador()
    {
        var hasher = HasherQueDelegaAoFake();
        var usuario = UsuarioDeSenha("senhaCorreta");
        var repo = RepoCom(usuario);

        await LoginFalha(repo, hasher, "senhaErrada");

        Received.InOrder(() =>
        {
            repo.ReservarTentativaDeSenhaAsync(usuario.Id, Arg.Any<DateTime>());
            hasher.Verify("senhaErrada", usuario.SenhaHash);
        });
        // Gravar a linha lida antes da reserva devolveria o contador ao valor velho (perde falhas paralelas).
        await repo.DidNotReceive().UpdateAsync(Arg.Any<Usuario>());
    }

    [Fact]
    public async Task LoginComReservaRecusadaPeloBancoNaoConfereASenha()
    {
        // Outra requisição paralela levou a última tentativa: a linha lida ainda parece livre, o banco não.
        var hasher = HasherQueDelegaAoFake();
        var usuario = UsuarioDeSenha("senhaCorreta");

        var recusa = await LoginFalha(RepoCom(usuario, reservaAceita: false), hasher, "senhaCorreta");

        recusa.Message.Should().Be(MensagemGenerica);
        hasher.DidNotReceive().Verify(Arg.Any<string>(), usuario.SenhaHash);
        hasher.Received(1).Verify("senhaCorreta", SenhaFicticia.Hash);
    }

    [Fact]
    public async Task Passo1ReservaATentativaAntesDeConferirASenhaENaoRegravaOContador()
    {
        var hasher = HasherQueDelegaAoFake();
        var usuario = UsuarioDeSenha("senhaCorreta");
        var repo = RepoCom(usuario);

        await Passo1Falha(repo, hasher, "senhaErrada");

        Received.InOrder(() =>
        {
            repo.ReservarTentativaDeSenhaAsync(usuario.Id, Arg.Any<DateTime>());
            hasher.Verify("senhaErrada", usuario.SenhaHash);
        });
        await repo.DidNotReceive().UpdateAsync(Arg.Any<Usuario>());
    }

    [Fact]
    public async Task Passo1ComReservaRecusadaPeloBancoNaoConfereASenha()
    {
        var hasher = HasherQueDelegaAoFake();
        var usuario = UsuarioDeSenha("senhaCorreta");

        var recusa = await Passo1Falha(RepoCom(usuario, reservaAceita: false), hasher, "senhaCorreta");

        recusa.Message.Should().Be(MensagemGenerica);
        hasher.DidNotReceive().Verify(Arg.Any<string>(), usuario.SenhaHash);
    }

    [Fact]
    public async Task Passo1ComSenhaCertaDevolveATentativaReservada()
    {
        // #1352: senha certa no passo 1 não conta falha (quem zera é o login completo).
        var usuario = UsuarioDeSenha("senhaCorreta");
        usuario.Empresas = [];
        var repo = RepoCom(usuario);

        await Passo1(repo, HasherQueDelegaAoFake()).ExecuteAsync(new ListarEmpresasParaLoginCommand(Email, "senhaCorreta"));

        await repo.Received(1).ReservarTentativaDeSenhaAsync(usuario.Id, Arg.Any<DateTime>());
        await repo.Received(1).DevolverTentativaDeSenhaAsync(usuario.Id);
    }
}

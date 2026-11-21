using EasyStock.Domain.Entities;
using EasyStock.Domain.Exceptions;
using EasyStock.Domain.ValueObjects;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities;

/// <summary>
/// #1352 (N7): a regra da falha de senha vive no domínio, uma só para o passo 1 do login
/// (lista-empresas) e para o login completo.
/// </summary>
public class UsuarioTests
{
    private static Usuario NovoUsuario() => Usuario.Criar("Ana", "ana@casadababa.com", "hash");

    [Fact]
    public void RegistrarFalhaDeSenhaBloqueiaNaQuintaPor15Minutos()
    {
        var usuario = NovoUsuario();

        for (var falha = 1; falha <= 4; falha++)
        {
            usuario.RegistrarFalhaDeSenha();

            usuario.FailedLoginAttempts.Should().Be(falha);
            usuario.EstaBloqueado().Should().BeFalse($"a falha {falha} ainda não bloqueia");
        }

        var antes = DateTime.UtcNow;
        usuario.RegistrarFalhaDeSenha();

        usuario.FailedLoginAttempts.Should().Be(5);
        usuario.EstaBloqueado().Should().BeTrue();
        usuario.LockoutEnd.Should().BeCloseTo(antes.AddMinutes(15), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void RegistrarFalhaDeSenhaComBloqueioVencidoRecomecaAContagem()
    {
        var usuario = NovoUsuario();
        usuario.FailedLoginAttempts = 5;
        usuario.LockoutEnd = DateTime.UtcNow.AddMinutes(-1);

        usuario.RegistrarFalhaDeSenha();

        usuario.FailedLoginAttempts.Should().Be(1, "a 5ª falha da janela anterior não herda para a nova");
        usuario.LockoutEnd.Should().BeNull();
        usuario.EstaBloqueado().Should().BeFalse();
    }

    // ── carimbo de revogação das sessões ──────────────────────────────────────────────────────

    private static DateTime Utc(int hora, int minuto, int segundo, int milissegundo = 0) =>
        new(2026, 10, 2, hora, minuto, segundo, milissegundo, DateTimeKind.Utc);

    [Fact]
    public void RevogarSessoesTruncaAoSegundoENuncaRecua()
    {
        var usuario = NovoUsuario();
        usuario.SessoesValidasDesde.Should().BeNull("nulo quer dizer que nunca revogou");

        usuario.RevogarSessoes(Utc(13, 45, 10, 789));
        usuario.SessoesValidasDesde.Should().Be(Utc(13, 45, 10));
        usuario.SessoesValidasDesde!.Value.Kind.Should().Be(DateTimeKind.Utc);

        usuario.RevogarSessoes(Utc(13, 44, 0));
        usuario.SessoesValidasDesde.Should().Be(Utc(13, 45, 10), "um instante anterior não recua o corte");

        usuario.RevogarSessoes(Utc(13, 45, 10, 999));
        usuario.SessoesValidasDesde.Should().Be(Utc(13, 45, 10), "o mesmo segundo não muda nada");

        usuario.RevogarSessoes(Utc(13, 45, 11, 1));
        usuario.SessoesValidasDesde.Should().Be(Utc(13, 45, 11), "um instante posterior avança o corte");
    }

    [Fact]
    public void RevogarSessoesAceitaInstanteLocalSemDeslocarOCorte()
    {
        var usuario = NovoUsuario();
        var local = Utc(13, 45, 10, 500).ToLocalTime();

        usuario.RevogarSessoes(local);

        usuario.SessoesValidasDesde.Should().Be(Utc(13, 45, 10));
    }

    [Fact]
    public void RegistrarFalhaDeSenhaNaoMexeNoCarimboDeSessoes()
    {
        var usuario = NovoUsuario();
        usuario.RevogarSessoes(Utc(13, 45, 10));

        usuario.RegistrarFalhaDeSenha();
        usuario.ResetarTentativasFalha();
        usuario.AtualizarUltimoAcesso();

        usuario.SessoesValidasDesde.Should().Be(Utc(13, 45, 10), "login e contagem de falha nunca mudam o corte");
    }
}

/// <summary>N9: o convidado nasce com hash inutilizável e só deixa de estar pendente ao aceitar o convite.</summary>
public class UsuarioConviteTests
{
    private static readonly DateTime Agora = new(2026, 10, 2, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CriarConvidadoNasceComHashInutilizavelESemEmailConfirmado()
    {
        var usuario = Usuario.CriarConvidado("Ana", "ana@casadababa.com");

        usuario.Ativo.Should().BeTrue();
        usuario.EmailConfirmado.Should().BeFalse();
        usuario.SenhaHash.Should().StartWith(Usuario.MarcadorDeConvite);
        usuario.ConviteAceitoEm.Should().BeNull();
        usuario.ConviteAceitoVia.Should().BeNull();
    }

    [Fact]
    public void ConvitePendenteDependeDoMarcador()
    {
        Usuario.CriarConvidado("Ana", "ana@casadababa.com").ConvitePendente.Should().BeTrue();
        Usuario.Criar("Ana", "ana@casadababa.com", "$2a$11$hashDeSenhaDeVerdade").ConvitePendente.Should().BeFalse();
    }

    [Fact]
    public void AceitarConviteTrocaOHashEGravaViaMascarada()
    {
        var usuario = Usuario.CriarConvidado("Ana", "ana@casadababa.com");
        usuario.DefinirTelefone(TelefoneE164.From("+5511999991234"));

        usuario.AceitarConvite("$2a$11$hashNovo", ViaDoConvite.WhatsApp(usuario.Telefone!), Agora);

        usuario.SenhaHash.Should().Be("$2a$11$hashNovo");
        usuario.ConvitePendente.Should().BeFalse();
        usuario.ConviteAceitoEm.Should().Be(Agora);
        usuario.ConviteAceitoVia.Should().Be("+55•••1234");
        usuario.ConviteAceitoVia.Should().NotContain("11999991234", "o telefone inteiro nunca vai para a coluna");
    }

    [Fact]
    public void AceitarPeloGoogleDeixaASenhaInutilizavelMasNaoPendente()
    {
        var usuario = Usuario.CriarConvidado("Ana", "ana@casadababa.com");

        usuario.AceitarConviteSemSenha(ViaDoConvite.Google, Agora);

        usuario.ConvitePendente.Should().BeFalse("o esqueci a senha passa a valer depois do aceite");
        usuario.SenhaHash.Should().StartWith("$2a$10$").And.NotStartWith(Usuario.MarcadorDeConvite);
        usuario.ConviteAceitoVia.Should().Be("Google");
        usuario.ConviteAceitoEm.Should().Be(Agora);
    }

    [Fact]
    public void AceitarConviteQueNaoEstaPendenteLanca()
    {
        var usuario = Usuario.Criar("Ana", "ana@casadababa.com", "$2a$11$hash");

        var act = () => usuario.AceitarConvite("$2a$11$novo", ViaDoConvite.Email, Agora);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ViaMascaraSoGuardaPaisEUltimosQuatroDigitos()
    {
        ViaDoConvite.WhatsApp(TelefoneE164.From("+5521988887777")).Should().Be("+55•••7777");
        ViaDoConvite.Email.Should().Be("e-mail");
        ViaDoConvite.Google.Should().Be("Google");
    }

    // ── #1781: o e-mail da conta é gravado normalizado (trim + minúsculas), igual ao que o login busca ──

    [Fact]
    public void CriarGravaOEmailNormalizado()
    {
        Usuario.Criar("Ana", "  Ana.Souza@CasaDaBaba.com ", "hash").Email.Should().Be("ana.souza@casadababa.com");
        Usuario.CriarConvidado("Leo", "LEO@casadababa.com").Email.Should().Be("leo@casadababa.com");
    }

    [Fact]
    public void SolicitarTrocaDeEmailGuardaOPendenteNormalizado()
    {
        var usuario = Usuario.Criar("Ana", "ana@casadababa.com", "hash");

        usuario.SolicitarTrocaDeEmail("  Nova@CasaDaBaba.COM ");
        usuario.ConfirmarNovoEmail();

        usuario.Email.Should().Be("nova@casadababa.com");
    }

    [Theory]
    [InlineData(" ADMIN@casadababa.demo ", "admin@casadababa.demo")]
    [InlineData("admin@casadababa.demo", "admin@casadababa.demo")]
    public void NormalizarEmailAplicaTrimEMinusculas(string informado, string esperado) =>
        Usuario.NormalizarEmail(informado).Should().Be(esperado);

    // #2695: a conta interna do sync móvel não é pessoa da equipe.
    [Fact]
    public void ContaDeSistemaNasceComLoginTravadoEReconhecida()
    {
        var empresaId = Guid.Parse("f2cf3584-4815-4883-a084-9aa56deebcbd");

        var conta = Usuario.CriarContaDeSistema(empresaId);

        conta.Email.Should().Be("mobile-sync+f2cf358448154883a0849aa56deebcbd@system.local");
        conta.Ativo.Should().BeFalse();
        conta.EhContaDeSistema.Should().BeTrue();
    }

    // #2695 (devolução da #2696): o domínio sozinho não identifica a conta; precisa do prefixo E da senha inutilizável.
    // Uma pessoa gravada com o domínio antes da guarda continua visível e desativável.
    [Theory]
    [InlineData("fulano@system.local", "$2a$11$hash-real")]
    [InlineData("mobile-sync+f2cf358448154883a0849aa56deebcbd@system.local", "$2a$11$hash-real")]
    [InlineData("fulano@system.local", "DISABLED_NO_LOGIN")]
    [InlineData("system.local@casadababa.com", "DISABLED_NO_LOGIN")]
    public void SoODominioNaoFazContaDeSistema(string email, string senhaHash)
    {
        var pessoa = new Usuario { Email = email, SenhaHash = senhaHash };

        pessoa.EhContaDeSistema.Should().BeFalse();
        Usuario.NaoEhContaDeSistema.Compile()(pessoa).Should().BeTrue("o filtro do repositório segue a mesma regra");
    }

    [Fact]
    public void FiltroDoRepositorioReconheceAContaDeSistema() =>
        Usuario.NaoEhContaDeSistema.Compile()(Usuario.CriarContaDeSistema(Guid.NewGuid())).Should().BeFalse();

    [Theory]
    [InlineData("fulano@system.local")]
    [InlineData(" Fulano@System.LOCAL ")]
    public void PessoaNaoNasceComODominioReservado(string email)
    {
        var criar = () => Usuario.Criar("Fulano", email, "hash");
        var convidar = () => Usuario.CriarConvidado("Fulano", email);

        criar.Should().Throw<EmailReservadoException>();
        convidar.Should().Throw<EmailReservadoException>();
    }

    [Fact]
    public void TrocaDeEmailNaoAceitaODominioReservado()
    {
        var ana = Usuario.Criar("Ana", "ana@casadababa.com", "hash");

        var pedir = () => ana.SolicitarTrocaDeEmail("ana@system.local");
        var trocarConvidado = () => ana.TrocarEmailDoConvidado("ana@system.local");

        pedir.Should().Throw<EmailReservadoException>();
        trocarConvidado.Should().Throw<EmailReservadoException>();
        ana.Email.Should().Be("ana@casadababa.com");
        ana.EmailPendente.Should().BeNull();
    }

    [Fact]
    public void TrocarEmailDoConvidadoGravaNormalizadoESemPendencia()
    {
        var ana = Usuario.CriarConvidado("Ana", "ana@casadababa.com");
        ana.EmailPendente = "outro@casadababa.com";
        ana.EmailConfirmado = true;

        ana.TrocarEmailDoConvidado(" Nova@CasaDaBaba.com ");

        ana.Email.Should().Be("nova@casadababa.com");
        ana.EmailPendente.Should().BeNull();
        ana.EmailConfirmado.Should().BeFalse();
    }
}

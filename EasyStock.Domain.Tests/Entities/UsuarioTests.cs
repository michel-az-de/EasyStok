using EasyStock.Domain.Entities;
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

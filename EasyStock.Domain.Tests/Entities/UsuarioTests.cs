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
}

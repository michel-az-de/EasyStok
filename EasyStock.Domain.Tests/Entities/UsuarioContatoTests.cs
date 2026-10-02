using EasyStock.Domain.Entities;
using EasyStock.Domain.ValueObjects;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities;

/// <summary>N4: telefone verificado e troca de e-mail em duas etapas no domínio do usuário.</summary>
public class UsuarioContatoTests
{
    private static readonly DateTime Agora = new(2026, 10, 2, 15, 0, 0, DateTimeKind.Utc);

    private static Usuario NovoUsuario()
    {
        var u = Usuario.Criar("Ana", "ana@casadababa.com", "hash");
        u.EmailConfirmado = true;
        return u;
    }

    [Fact]
    public void TrocarTelefoneZeraAVerificacao()
    {
        var u = NovoUsuario();
        u.DefinirTelefone(TelefoneE164.From("11997573992"));
        u.MarcarTelefoneVerificado(Agora);

        u.DefinirTelefone(TelefoneE164.From("11988887777"));

        u.Telefone!.Value.Should().Be("+5511988887777");
        u.TelefoneVerificadoEm.Should().BeNull();
    }

    [Fact]
    public void RepetirOMesmoTelefoneNaoDerrubaAVerificacao()
    {
        var u = NovoUsuario();
        u.DefinirTelefone(TelefoneE164.From("11997573992"));
        u.MarcarTelefoneVerificado(Agora);

        u.DefinirTelefone(TelefoneE164.From("(11) 99757-3992"));

        u.TelefoneVerificadoEm.Should().Be(Agora);
    }

    [Fact]
    public void VerificarTelefoneGravaOInstante()
    {
        var u = NovoUsuario();
        u.DefinirTelefone(TelefoneE164.From("11997573992"));

        u.MarcarTelefoneVerificado(Agora);

        u.TelefoneVerificadoEm.Should().Be(Agora);
    }

    [Fact]
    public void VerificarSemTelefoneRecusa() =>
        FluentActions.Invoking(() => NovoUsuario().MarcarTelefoneVerificado(Agora))
            .Should().Throw<InvalidOperationException>();

    [Fact]
    public void SolicitarTrocaDeEmailNaoAlteraOEmailNemAConfirmacao()
    {
        var u = NovoUsuario();

        u.SolicitarTrocaDeEmail("  nova@casadababa.com ");

        u.EmailPendente.Should().Be("nova@casadababa.com");
        u.Email.Should().Be("ana@casadababa.com");
        u.EmailConfirmado.Should().BeTrue();
    }

    [Fact]
    public void ConfirmarNovoEmailTrocaEZeraAPendencia()
    {
        var u = NovoUsuario();
        u.SolicitarTrocaDeEmail("nova@casadababa.com");

        u.ConfirmarNovoEmail();

        u.Email.Should().Be("nova@casadababa.com");
        u.EmailPendente.Should().BeNull();
        u.EmailConfirmado.Should().BeTrue();
    }

    [Fact]
    public void ConfirmarSemPendenciaRecusa() =>
        FluentActions.Invoking(() => NovoUsuario().ConfirmarNovoEmail()).Should().Throw<InvalidOperationException>();

    [Fact]
    public void AnonimizarLimpaTelefoneVerificacaoEEmailPendente()
    {
        var u = NovoUsuario();
        u.DefinirTelefone(TelefoneE164.From("11997573992"));
        u.MarcarTelefoneVerificado(Agora);
        u.SolicitarTrocaDeEmail("nova@casadababa.com");

        u.Anonimizar();

        u.Telefone.Should().BeNull();
        u.TelefoneVerificadoEm.Should().BeNull();
        u.EmailPendente.Should().BeNull();
    }
}

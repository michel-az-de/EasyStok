using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Exceptions;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Atendimento;

/// <summary>
/// #2833: a loja reage a qualquer mensagem que tem id no canal e apaga para todos só a mensagem que ela enviou,
/// até 48 h depois do envio. O registro fica guardado como apagado, com quem apagou e quando.
/// </summary>
public class MensagemReacaoEApagarTests
{
    private static readonly DateTime Agora = new(2026, 11, 18, 14, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Usuario = Guid.NewGuid();

    private static Mensagem Enviada(DateTime em) =>
        Mensagem.Saida(Guid.NewGuid(), Guid.NewGuid(), AutorMensagem.Dona, em, TipoConteudoMensagem.Texto, "Mando o Pix?", "wpp:s:true_5511@c.us_A");

    private static Mensagem DoCliente() =>
        Mensagem.Entrada(Guid.NewGuid(), Guid.NewGuid(), Agora, TipoConteudoMensagem.Texto, "Quero 2", "wpp:s:false_5511@c.us_B");

    [Fact]
    public void Apagar_para_todos_guarda_quem_e_quando_e_mantem_o_texto()
    {
        var m = Enviada(Agora.AddHours(-2));

        m.ApagarParaTodos(Usuario, Agora);

        m.ApagadaEm.Should().Be(Agora);
        m.ApagadaPorUsuarioId.Should().Be(Usuario);
        m.Texto.Should().Be("Mando o Pix?");
    }

    [Fact]
    public void Apagar_aceita_ate_48_horas_e_recusa_depois()
    {
        Enviada(Agora.AddHours(-48)).Invoking(m => m.ValidarApagarParaTodos(Agora)).Should().NotThrow();

        var velha = Enviada(Agora.AddHours(-48).AddSeconds(-1));
        velha.Invoking(m => m.ApagarParaTodos(Usuario, Agora))
            .Should().Throw<RegraDeDominioVioladaException>().WithMessage("*48 h*");
        velha.ApagadaEm.Should().BeNull();
    }

    [Fact]
    public void Mensagem_do_cliente_nao_pode_ser_apagada()
    {
        DoCliente().Invoking(m => m.ApagarParaTodos(Usuario, Agora))
            .Should().Throw<RegraDeDominioVioladaException>().WithMessage("*loja*");
    }

    [Fact]
    public void Mensagem_sem_id_no_canal_nao_pode_ser_apagada_nem_reagida()
    {
        var pendente = Mensagem.Saida(Guid.NewGuid(), Guid.NewGuid(), AutorMensagem.Dona, Agora, TipoConteudoMensagem.Texto, "Oi");

        pendente.Invoking(m => m.ApagarParaTodos(Usuario, Agora)).Should().Throw<RegraDeDominioVioladaException>();
        pendente.Invoking(m => m.ValidarReacao("👍")).Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void Apagar_duas_vezes_e_recusado()
    {
        var m = Enviada(Agora.AddMinutes(-5));
        m.ApagarParaTodos(Usuario, Agora);

        m.Invoking(x => x.ApagarParaTodos(Usuario, Agora.AddMinutes(1)))
            .Should().Throw<RegraDeDominioVioladaException>().WithMessage("*já foi apagada*");
        m.ApagadaEm.Should().Be(Agora);
    }

    [Fact]
    public void Reagir_grava_o_emoji_e_vazio_retira()
    {
        var m = DoCliente();

        m.Reagir(" 👍 ");
        m.Reacao.Should().Be("👍");

        m.Reagir(null);
        m.Reacao.Should().BeNull();
    }

    [Theory]
    [InlineData("ok")]
    [InlineData("👍 👍")]
    [InlineData("👍👍👍👍👍👍👍👍👍👍👍👍👍👍👍👍👍")]
    public void Reacao_precisa_ser_um_emoji_curto(string emoji)
    {
        DoCliente().Invoking(m => m.ValidarReacao(emoji)).Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void Mensagem_apagada_nao_recebe_reacao()
    {
        var m = Enviada(Agora.AddMinutes(-5));
        m.ApagarParaTodos(Usuario, Agora);

        m.Invoking(x => x.Reagir("❤️")).Should().Throw<RegraDeDominioVioladaException>();
    }
}

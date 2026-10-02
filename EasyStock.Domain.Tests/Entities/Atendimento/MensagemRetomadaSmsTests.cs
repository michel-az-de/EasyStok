using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Exceptions;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Atendimento;

/// <summary>
/// S58 (#1391): fora da janela a mensagem espera o cliente responder ao modelo de retomada e é liberada
/// para reenvio quando ele responde. S60: a reserva por SMS sai uma vez, só quando o WhatsApp desistiu
/// e a falha não é incerta.
/// </summary>
public class MensagemRetomadaSmsTests
{
    private static readonly DateTime Agora = new(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);

    private static Mensagem Falhou(TipoFalhaEnvio tipo = TipoFalhaEnvio.Permanente)
    {
        var m = Mensagem.Saida(Guid.NewGuid(), Guid.NewGuid(), AutorMensagem.Agente, Agora, TipoConteudoMensagem.Texto, "Oi!");
        m.RegistrarFalhaEnvio("erro", tipo, Agora);
        return m;
    }

    [Fact]
    public void AguardaOClienteSemReenvioAgendado()
    {
        var m = Falhou(TipoFalhaEnvio.Temporaria);

        m.AguardarCliente(Agora, "Fora da janela de 24 h.");

        m.AguardaClienteDesde.Should().Be(Agora);
        m.ProximoReenvioEm.Should().BeNull();
        m.Erro.Should().Be("Fora da janela de 24 h.");
        m.Status.Should().Be(StatusMensagem.Falhou);
    }

    [Fact]
    public void ClienteRespondeuLiberaParaReenvioNaHora()
    {
        var m = Falhou();
        m.AguardarCliente(Agora, "x");

        m.LiberarAposResposta(Agora.AddHours(30));

        m.AguardaClienteDesde.Should().BeNull();
        m.ProximoReenvioEm.Should().Be(Agora.AddHours(30));
    }

    [Fact]
    public void ReenviadaDeixaDeAguardar()
    {
        var m = Falhou();
        m.AguardarCliente(Agora, "x");

        m.RegistrarReenviada("wamid.2");

        m.AguardaClienteDesde.Should().BeNull();
    }

    [Fact]
    public void SoAguardaQuemPodeReenviar()
    {
        var enviada = Mensagem.Saida(Guid.NewGuid(), Guid.NewGuid(), AutorMensagem.Agente, Agora, TipoConteudoMensagem.Texto, "Oi", "wamid.1");

        var act = () => enviada.AguardarCliente(Agora, "x");

        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Theory]
    [InlineData(TipoFalhaEnvio.Permanente, true)]
    [InlineData(TipoFalhaEnvio.Incerta, false)]
    public void ReservaSmsSoQuandoOWhatsAppDesistiuSemDuvida(TipoFalhaEnvio tipo, bool precisa)
    {
        Falhou(tipo).PrecisaDeReservaSms.Should().Be(precisa);
    }

    [Fact]
    public void ReservaSmsEsperaOsReenviosAutomaticosAcabarem()
    {
        var m = Falhou(TipoFalhaEnvio.Temporaria);
        m.PrecisaDeReservaSms.Should().BeFalse("ainda há reenvio agendado");

        for (var i = 0; i < 4; i++) m.RegistrarFalhaEnvio("erro", TipoFalhaEnvio.Temporaria, Agora);

        m.ProximoReenvioEm.Should().BeNull();
        m.PrecisaDeReservaSms.Should().BeTrue();
    }

    [Fact]
    public void ReservaSmsNaoSaiEnquantoAguardaOCliente()
    {
        var m = Falhou();
        m.AguardarCliente(Agora, "x");

        m.PrecisaDeReservaSms.Should().BeFalse();
    }

    [Fact]
    public void ReservaSmsSaiUmaVezSo()
    {
        var m = Falhou();

        m.RegistrarReservaSms(Agora);

        m.ReservaSmsEm.Should().Be(Agora);
        m.PrecisaDeReservaSms.Should().BeFalse();
        var deNovo = () => m.RegistrarReservaSms(Agora);
        deNovo.Should().Throw<RegraDeDominioVioladaException>();
    }
}

/// <summary>S58: o modelo de retomada é configurado pela empresa; nome no formato da Meta.</summary>
public class ConfiguracaoModeloRetomadaTests
{
    [Fact]
    public void SemModeloPorPadrao() =>
        ConfiguracaoAtendimento.CriarPadrao(Guid.NewGuid()).ModeloRetomada.Should().BeNull();

    [Fact]
    public void DefineELimpa()
    {
        var c = ConfiguracaoAtendimento.CriarPadrao(Guid.NewGuid());

        c.DefinirModeloRetomada(" retomar_conversa ", null);
        c.ModeloRetomada.Should().Be(new ModeloRetomada("retomar_conversa", "pt_BR"));

        c.DefinirModeloRetomada(null, null);
        c.ModeloRetomada.Should().BeNull();
    }

    [Theory]
    [InlineData("Retomar Conversa")]
    [InlineData("retomar-conversa")]
    public void RecusaNomeForaDoFormatoDaMeta(string nome)
    {
        var act = () => ConfiguracaoAtendimento.CriarPadrao(Guid.NewGuid()).DefinirModeloRetomada(nome, "pt_BR");

        act.Should().Throw<RegraDeDominioVioladaException>();
    }
}

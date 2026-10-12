using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Atendimento;

/// <summary>
/// S57 (#1355): falha temporária agenda reenvio em 1, 5, 15 e 60 min (até 6 h); permanente e incerta não
/// agendam. O reenvio que dá certo vira Enviada com o id da Meta.
/// </summary>
public class MensagemReenvioTests
{
    private static readonly DateTime Agora = new(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);

    private static Mensagem Saida() =>
        Mensagem.Saida(Guid.NewGuid(), Guid.NewGuid(), AutorMensagem.Agente, Agora, TipoConteudoMensagem.Texto, "Oi!");

    [Fact]
    public void FalhaTemporariaAgendaReenvioComEsperaCrescente()
    {
        var m = Saida();

        m.RegistrarFalhaEnvio("Meta fora", TipoFalhaEnvio.Temporaria, Agora);
        m.ProximoReenvioEm.Should().Be(Agora.AddMinutes(1));
        m.RegistrarFalhaEnvio("Meta fora", TipoFalhaEnvio.Temporaria, Agora.AddMinutes(1));
        m.ProximoReenvioEm.Should().Be(Agora.AddMinutes(1 + 5));

        m.Status.Should().Be(StatusMensagem.Falhou);
        m.TentativasEnvio.Should().Be(2);
        m.Erro.Should().Be("Meta fora");
    }

    [Fact]
    public void ParaDepoisDoQuartoReenvio()
    {
        // Envio original + 4 reenvios (1, 5, 15 e 60 min): a 5ª falha encerra.
        var m = Saida();
        for (var i = 0; i < 4; i++) m.RegistrarFalhaEnvio("Meta fora", TipoFalhaEnvio.Temporaria, Agora);
        m.ProximoReenvioEm.Should().Be(Agora.AddMinutes(60));

        m.RegistrarFalhaEnvio("Meta fora", TipoFalhaEnvio.Temporaria, Agora);

        m.TentativasEnvio.Should().Be(5);
        m.ProximoReenvioEm.Should().BeNull();
    }

    [Fact]
    public void NaoAgendaDepoisDeSeisHoras()
    {
        var m = Saida();

        m.RegistrarFalhaEnvio("Meta fora", TipoFalhaEnvio.Temporaria, Agora.AddHours(6).AddMinutes(1));

        m.ProximoReenvioEm.Should().BeNull();
    }

    [Theory]
    [InlineData(TipoFalhaEnvio.Permanente)]
    [InlineData(TipoFalhaEnvio.Incerta)]
    public void PermanenteOuIncertaNaoAgenda(TipoFalhaEnvio tipo)
    {
        var m = Saida();

        m.RegistrarFalhaEnvio("erro", tipo, Agora);

        m.ProximoReenvioEm.Should().BeNull();
        m.Status.Should().Be(StatusMensagem.Falhou);
    }

    [Fact]
    public void IncertaAvisaNoErro()
    {
        var m = Saida();

        m.RegistrarFalhaEnvio("timeout", TipoFalhaEnvio.Incerta, Agora);

        m.Erro.Should().StartWith("Envio incerto");
    }

    [Fact]
    public void ReenvioQueDaCertoViraEnviada()
    {
        var m = Saida();
        m.RegistrarFalhaEnvio("Meta fora", TipoFalhaEnvio.Temporaria, Agora);

        m.ReservarReenvio();
        m.ProximoReenvioEm.Should().BeNull();
        m.RegistrarReenviada("wamid.novo");

        m.Status.Should().Be(StatusMensagem.Enviada);
        m.ExternoId.Should().Be("wamid.novo");
        m.Erro.Should().BeNull();
    }

    [Fact]
    public void SoReenviaTextoQueFalhou()
    {
        var enviada = Mensagem.Saida(Guid.NewGuid(), Guid.NewGuid(), AutorMensagem.Agente, Agora,
            TipoConteudoMensagem.Texto, "Oi!", "wamid.1");
        var falhou = Saida();
        falhou.RegistrarFalhaEnvio("x", TipoFalhaEnvio.Permanente, Agora);

        enviada.PodeReenviar.Should().BeFalse();
        falhou.PodeReenviar.Should().BeTrue();
    }
    [Fact]
    public void FalhaTemporariaDeImagemNaoAgendaReenvio()
    {
        // #1411: só texto pode ser reenviado; agendar imagem deixaria a varredura presa nela.
        var m = Mensagem.Saida(Guid.NewGuid(), Guid.NewGuid(), AutorMensagem.Agente, Agora, TipoConteudoMensagem.Imagem, null);

        m.RegistrarFalhaEnvio("Meta fora", TipoFalhaEnvio.Temporaria, Agora);

        m.Status.Should().Be(StatusMensagem.Falhou);
        m.ProximoReenvioEm.Should().BeNull();
    }
}

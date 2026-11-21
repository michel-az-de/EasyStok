using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Exceptions;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Atendimento;

/// <summary>
/// #3068: a equipe não respondeu no prazo e o Assistente retoma a conversa. A retomada fica amarrada à entrada do
/// cliente (no <c>ContextoJson</c>, sem coluna nova) para valer uma vez só por mensagem, e não se confunde com a
/// devolução feita pela equipe (#2493).
/// </summary>
public class ConversaRetomadaPorTempoTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly DateTime Agora = new(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);

    private static Conversa Escalada(CanalConversa canal = CanalConversa.WhatsApp)
    {
        var conversa = Conversa.Abrir(Empresa, "5511999990004", Agora, contatoNome: "Tatiana", canal: canal);
        conversa.RegistrarEntrada(Agora);
        conversa.Escalar(Agora.AddSeconds(5), "cliente pediu para falar com uma pessoa");
        return conversa;
    }

    [Fact]
    public void RetomadaDevolveAConversaAoAssistenteSemResponsavelNemMotivo()
    {
        var conversa = Conversa.Abrir(Empresa, "5511999990005", Agora, contatoNome: "Tatiana");
        conversa.RegistrarEntrada(Agora);
        conversa.Assumir(Agora.AddSeconds(5), Guid.NewGuid());

        conversa.RetomarPorTempo(Guid.NewGuid());

        conversa.Situacao.Should().Be(SituacaoConversa.Automatica);
        conversa.AssumidaPorUsuarioId.Should().BeNull("a conversa com responsável nominal também volta");
        conversa.MotivoEscalada.Should().BeNull();
    }

    [Fact]
    public void RetomadaNaoLigaOSinalDaDevolucaoFeitaPelaEquipe()
    {
        var conversa = Escalada();

        conversa.RetomarPorTempo(Guid.NewGuid());

        conversa.AutomaticoLiberadoPelaEquipe.Should().BeFalse(
            "senão o robô passaria a responder o contato da equipe para sempre (#2493)");
    }

    [Fact]
    public void RetomadaValeUmaVezPorEntradaDoCliente()
    {
        var entrada = Guid.NewGuid();
        var conversa = Escalada();

        conversa.RetomadaPorTempoDecidida(entrada).Should().BeFalse();
        conversa.RetomarPorTempo(entrada);

        conversa.RetomadaPorTempoDecidida(entrada).Should().BeTrue();
        conversa.RetomadaPorTempoDecidida(Guid.NewGuid()).Should().BeFalse("mensagem nova do cliente abre outra retomada");
    }

    [Fact]
    public void EscalarDeNovoNaoApagaAGuardaDaEntradaRetomada()
    {
        var entrada = Guid.NewGuid();
        var conversa = Escalada();
        conversa.RetomarPorTempo(entrada);

        conversa.Escalar(Agora.AddMinutes(2), "cliente insiste em falar com uma pessoa");

        conversa.RetomadaPorTempoDecidida(entrada).Should().BeTrue("o robô não retoma em laço a mesma mensagem");
    }

    [Fact]
    public void DispensaMarcaAEntradaSemTirarAConversaDaEquipe()
    {
        var entrada = Guid.NewGuid();
        var conversa = Escalada();

        conversa.DispensarRetomadaPorTempo(entrada);

        conversa.RetomadaPorTempoDecidida(entrada).Should().BeTrue("a varredura não reavalia a mesma mensagem");
        conversa.Situacao.Should().Be(SituacaoConversa.Assumida);
        conversa.MotivoEscalada.Should().Be("cliente pediu para falar com uma pessoa");
        Conversa.RetomadaPorTempoDecidida(conversa.ContextoJson, entrada).Should().BeTrue();
    }

    [Fact]
    public void ConversaQueJaEstaComOAssistenteRecusaARetomada()
    {
        var conversa = Conversa.Abrir(Empresa, "5511999990006", Agora, contatoNome: "Tatiana");
        conversa.RegistrarEntrada(Agora);

        var acao = () => conversa.RetomarPorTempo(Guid.NewGuid());

        acao.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void CanalSemAgenteRecusaARetomada()
    {
        var conversa = Escalada(CanalConversa.Instagram);

        var acao = () => conversa.RetomarPorTempo(Guid.NewGuid());

        acao.Should().Throw<RegraDeDominioVioladaException>("ninguém responderia nesse canal");
        conversa.Situacao.Should().Be(SituacaoConversa.Assumida);
    }

    [Fact]
    public void ConversaEncerradaRecusaARetomada()
    {
        var conversa = Escalada();
        conversa.Encerrar(Agora.AddMinutes(1));

        var acao = () => conversa.RetomarPorTempo(Guid.NewGuid());

        acao.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void EntradaVaziaRecusaARetomada()
    {
        var conversa = Escalada();

        var acao = () => conversa.RetomarPorTempo(Guid.Empty);

        acao.Should().Throw<RegraDeDominioVioladaException>();
        conversa.Situacao.Should().Be(SituacaoConversa.Assumida);
    }
}

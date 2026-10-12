using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Exceptions;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Atendimento;

/// <summary>
/// Atribuição de conversa (S41): transferir troca o responsável e deixa a conversa assumida (o agente
/// cala). A mensagem de saída humana guarda quem enviou.
/// </summary>
public class ConversaTransferenciaTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly DateTime Agora = new(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);

    private static Conversa Nova() => Conversa.Abrir(Empresa, "5511999990001", Agora, contatoNome: "Tatiana");

    [Fact]
    public void Transferir_TrocaResponsavelEAssume()
    {
        var conversa = Nova();
        var ana = Guid.NewGuid();
        var bia = Guid.NewGuid();
        conversa.Assumir(Agora.AddMinutes(1), ana);

        conversa.Transferir(bia, Agora.AddMinutes(2));

        conversa.Situacao.Should().Be(SituacaoConversa.Assumida);
        conversa.AssumidaPorUsuarioId.Should().Be(bia);
    }

    [Fact]
    public void Transferir_ConversaAutomatica_PassaAAssumida()
    {
        var conversa = Nova();
        var bia = Guid.NewGuid();

        conversa.Transferir(bia, Agora.AddMinutes(2));

        conversa.Situacao.Should().Be(SituacaoConversa.Assumida);
        conversa.AssumidaPorUsuarioId.Should().Be(bia);
    }

    [Fact]
    public void Transferir_SemDestino_Lanca()
    {
        var act = () => Nova().Transferir(Guid.Empty, Agora);
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void Transferir_Encerrada_Lanca()
    {
        var conversa = Nova();
        conversa.Encerrar(Agora);

        var act = () => conversa.Transferir(Guid.NewGuid(), Agora.AddMinutes(1));
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void MensagemDaDona_GuardaQuemEnviou()
    {
        var usuario = Guid.NewGuid();
        var msg = Mensagem.Saida(Empresa, Guid.NewGuid(), AutorMensagem.Dona, Agora, TipoConteudoMensagem.Texto, "oi", "wamid.1");

        msg.RegistrarEnviadaPor(usuario);

        msg.EnviadaPorUsuarioId.Should().Be(usuario);
    }

    [Theory]
    [InlineData(AutorMensagem.Agente)]
    [InlineData(AutorMensagem.Sistema)]
    public void MensagemQueNaoEhHumana_NaoAceitaQuemEnviou(AutorMensagem autor)
    {
        var msg = Mensagem.Saida(Empresa, Guid.NewGuid(), autor, Agora, TipoConteudoMensagem.Texto, "oi");

        var act = () => msg.RegistrarEnviadaPor(Guid.NewGuid());
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void MensagemDaDona_SemUsuario_Lanca()
    {
        var msg = Mensagem.Saida(Empresa, Guid.NewGuid(), AutorMensagem.Dona, Agora, TipoConteudoMensagem.Texto, "oi");

        var act = () => msg.RegistrarEnviadaPor(Guid.Empty);
        act.Should().Throw<RegraDeDominioVioladaException>();
    }
}

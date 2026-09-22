using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Exceptions;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Atendimento;

/// <summary>
/// Testes da entity <see cref="Mensagem"/> (S04, ADR-0050): entrada vem do
/// cliente e ja chegou; saida nasce pendente ate a Meta devolver o <c>wamid</c>
/// e depois so avanca de status (a Meta pode entregar "read" antes de
/// "delivered"; um status menor nunca regride o maior).
/// </summary>
public class MensagemTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Conversa = Guid.NewGuid();
    private static readonly DateTime Em = new(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Entrada_TemAutorClienteEStatusEntregue()
    {
        var msg = Mensagem.Entrada(Empresa, Conversa, Em, TipoConteudoMensagem.Texto, "oi, tem lasanha?", externoId: "wamid.1");

        msg.Id.Should().NotBeEmpty();
        msg.Direcao.Should().Be(DirecaoMensagem.Entrada);
        msg.Autor.Should().Be(AutorMensagem.Cliente);
        msg.Status.Should().Be(StatusMensagem.Entregue, "mensagem recebida ja esta entregue a nos");
        msg.Texto.Should().Be("oi, tem lasanha?");
        msg.ExternoId.Should().Be("wamid.1");
        msg.EnviadaEm.Should().Be(Em);
        msg.ProcessadaEm.Should().BeNull();
    }

    [Fact]
    public void Saida_ComAutorCliente_Lanca()
    {
        var act = () => Mensagem.Saida(Empresa, Conversa, AutorMensagem.Cliente, Em, TipoConteudoMensagem.Texto, "x");

        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void Saida_NascePendenteEConfirmaEnvioComWamid()
    {
        var msg = Mensagem.Saida(Empresa, Conversa, AutorMensagem.Agente, Em, TipoConteudoMensagem.Texto, "temos sim!");

        msg.Status.Should().Be(StatusMensagem.Pendente);
        msg.ExternoId.Should().BeNull();

        msg.ConfirmarEnvio("wamid.2");

        msg.Status.Should().Be(StatusMensagem.Enviada);
        msg.ExternoId.Should().Be("wamid.2");
    }

    [Fact]
    public void ConfirmarEnvio_EmEntrada_Lanca()
    {
        var msg = Mensagem.Entrada(Empresa, Conversa, Em, TipoConteudoMensagem.Texto, "oi");

        var act = () => msg.ConfirmarEnvio("wamid.3");

        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void StatusDeEntrega_NaoRegrideEFalhouGuardaErro()
    {
        var msg = Mensagem.Saida(Empresa, Conversa, AutorMensagem.Dona, Em, TipoConteudoMensagem.Texto, "saiu para entrega", externoId: "wamid.4");

        msg.AtualizarStatusEntrega(StatusMensagem.Lida);
        msg.AtualizarStatusEntrega(StatusMensagem.Entregue);
        msg.Status.Should().Be(StatusMensagem.Lida, "delivered depois de read nao regride");

        msg.AtualizarStatusEntrega(StatusMensagem.Falhou, erro: "131047 fora da janela");
        msg.Status.Should().Be(StatusMensagem.Falhou);
        msg.Erro.Should().Contain("131047");
    }

    [Fact]
    public void Texto_ObrigatorioParaTipoTextoELimitadoA4096()
    {
        var semTexto = () => Mensagem.Entrada(Empresa, Conversa, Em, TipoConteudoMensagem.Texto, "   ");
        semTexto.Should().Throw<RegraDeDominioVioladaException>();

        var longo = () => Mensagem.Entrada(Empresa, Conversa, Em, TipoConteudoMensagem.Texto, new string('a', Mensagem.TextoTamanhoMaximo + 1));
        longo.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void Botao_ExigeBotaoIdEMidiaAnexaChaveEMime()
    {
        var semId = () => Mensagem.Entrada(Empresa, Conversa, Em, TipoConteudoMensagem.Botao, "Sim");
        semId.Should().Throw<RegraDeDominioVioladaException>();

        var botao = Mensagem.Entrada(Empresa, Conversa, Em, TipoConteudoMensagem.Botao, "Sim", botaoId: "acao:confirmar_endereco:abc");
        botao.BotaoId.Should().Be("acao:confirmar_endereco:abc");

        var imagem = Mensagem.Entrada(Empresa, Conversa, Em, TipoConteudoMensagem.Imagem, texto: null, externoId: "wamid.5");
        imagem.AnexarMidia("atendimento/e/c/wamid.5.jpg", "image/jpeg");
        imagem.MarcarProcessada(Em.AddSeconds(3));

        imagem.MidiaChave.Should().Be("atendimento/e/c/wamid.5.jpg");
        imagem.MidiaMime.Should().Be("image/jpeg");
        imagem.ProcessadaEm.Should().Be(Em.AddSeconds(3));
    }
}

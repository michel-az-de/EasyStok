using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Tests.Services.Atendimento;

/// <summary>
/// #2833 com #3068: o que reagir e apagar mudam para o Assistente. A mensagem apagada para todos sai do histórico que
/// vai para a IA (o cliente não a vê mais); a reação da loja não é resposta, então não segura a retomada por tempo.
/// </summary>
public class AcoesNaMensagemEAssistenteTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly DateTime Agora = new(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);

    private static string TextoDe(IEnumerable<MensagemLlm> conversacao) =>
        string.Join("\n", conversacao.SelectMany(m => m.Conteudo.OfType<BlocoTextoLlm>()).Select(b => b.Texto));

    [Fact]
    public void MensagemApagadaParaTodos_NaoEntraNoHistoricoDaIa()
    {
        var conversaId = Guid.NewGuid();
        var pergunta = Mensagem.Entrada(Empresa, conversaId, Agora, TipoConteudoMensagem.Texto, "Qual a chave do Pix?", "wamid.1");
        var errada = Mensagem.Saida(Empresa, conversaId, AutorMensagem.Atendente, Agora.AddMinutes(1), TipoConteudoMensagem.Texto,
            "A chave é 11999990000", "wamid.2");
        var certa = Mensagem.Saida(Empresa, conversaId, AutorMensagem.Atendente, Agora.AddMinutes(2), TipoConteudoMensagem.Texto,
            "A chave é pix@loja.example", "wamid.3");
        errada.ApagarParaTodos(Guid.NewGuid(), Agora.AddMinutes(3));

        var texto = TextoDe(GeracaoAgenteAtendimento.MontarConversacao([pergunta, errada, certa], []));

        texto.Should().Contain("pix@loja.example").And.NotContain("11999990000");
    }

    [Fact]
    public void ReacaoDaLoja_NaoContaComoRespostaParaARetomada()
    {
        var conversa = Conversa.Abrir(Empresa, "5511999990007", Agora, contatoNome: "Tatiana");
        conversa.RegistrarEntrada(Agora);
        conversa.Assumir(Agora.AddSeconds(5), Guid.NewGuid());
        var entrada = Mensagem.Entrada(Empresa, conversa.Id, Agora, TipoConteudoMensagem.Texto, "Tem bolo de cenoura hoje?", "wamid.4");
        entrada.Reagir("👍");

        var pendente = RetomadaDoAssistente.EntradaSemResposta(conversa, [entrada], Agora.AddMinutes(5), TimeSpan.FromSeconds(60));

        pendente.Should().BeSameAs(entrada);
    }

    [Fact]
    public void RespostaApagadaParaTodos_NaoContaComoRespostaParaARetomada()
    {
        var conversa = Conversa.Abrir(Empresa, "5511999990008", Agora, contatoNome: "Tatiana");
        conversa.RegistrarEntrada(Agora);
        conversa.Assumir(Agora.AddSeconds(5), Guid.NewGuid());
        var entrada = Mensagem.Entrada(Empresa, conversa.Id, Agora, TipoConteudoMensagem.Texto, "Quanto fica o frete?", "wamid.5");
        var resposta = Mensagem.Saida(Empresa, conversa.Id, AutorMensagem.Atendente, Agora.AddSeconds(20), TipoConteudoMensagem.Texto,
            "R$ 5", "wamid.6");
        resposta.ApagarParaTodos(Guid.NewGuid(), Agora.AddSeconds(30));

        var pendente = RetomadaDoAssistente.EntradaSemResposta(conversa, [entrada, resposta], Agora.AddMinutes(5), TimeSpan.FromSeconds(60));

        pendente.Should().BeSameAs(entrada);
    }

    [Fact]
    public void ApagarAResposta_DaOPrazoInteiroAQuemApagou()
    {
        var prazo = TimeSpan.FromSeconds(60);
        var conversa = Conversa.Abrir(Empresa, "5511999990009", Agora, contatoNome: "Tatiana");
        conversa.RegistrarEntrada(Agora);
        conversa.Assumir(Agora.AddSeconds(2), Guid.NewGuid());
        var entrada = Mensagem.Entrada(Empresa, conversa.Id, Agora, TipoConteudoMensagem.Texto, "Quanto fica o frete?", "wamid.7");
        var resposta = Mensagem.Saida(Empresa, conversa.Id, AutorMensagem.Atendente, Agora.AddSeconds(5), TipoConteudoMensagem.Texto,
            "R$ 5", "wamid.8");
        var apagouEm = Agora.AddMinutes(3);
        resposta.ApagarParaTodos(Guid.NewGuid(), apagouEm);

        // Quem apagou está corrigindo a resposta: o relógio recomeça no apagar, não no envio da mensagem apagada.
        RetomadaDoAssistente.EntradaSemResposta(conversa, [entrada, resposta], apagouEm.AddSeconds(30), prazo).Should().BeNull();
        RetomadaDoAssistente.EntradaSemResposta(conversa, [entrada, resposta], apagouEm.AddSeconds(61), prazo).Should().BeSameAs(entrada);
    }
}

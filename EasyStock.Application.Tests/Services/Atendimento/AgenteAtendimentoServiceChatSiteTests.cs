using System.Text.Json;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento.Ferramentas;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Tests.Services.Atendimento;

/// <summary>#2876: no chat do site a resposta do agente vira mensagem gravada, sem passar pelo WhatsApp.</summary>
public partial class AgenteAtendimentoServiceTests
{
    [Fact]
    public async Task RespostaNoChatSite_GravaSaidaDoAssistenteSemPassarPeloWhatsApp()
    {
        var conversa = Conversa.Abrir(_empresaId, "sessao-1", Agora.AddMinutes(-1), "Visitante", canal: CanalConversa.ChatSite);
        conversa.RegistrarEntrada(Agora.AddMinutes(-1));
        var entradaSite = Mensagem.Entrada(_empresaId, conversa.Id, Agora.AddMinutes(-1), TipoConteudoMensagem.Texto,
            "quero falar com gente", "chatsite:in1");
        _conversaRepository.ObterComMensagensAsync(_empresaId, conversa.Id, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new ConversaComMensagens(conversa, [entradaSite]));
        _conversaRepository.ObterSituacaoAsync(_empresaId, conversa.Id, Arg.Any<CancellationToken>()).Returns(SituacaoConversa.Automatica);
        _conversaRepository.ObterPorIdComLockAsync(_empresaId, conversa.Id, Arg.Any<CancellationToken>()).Returns(conversa);
        LojaLigada(nomeDona: "Michele");
        _escalador.When(e => e.EscalarAsync(_empresaId, conversa, Arg.Any<string>(), Agora, Arg.Any<CancellationToken>()))
            .Do(_ => conversa.Escalar(Agora, "quer falar com gente"));
        var entrada = JsonSerializer.SerializeToElement(new { motivo = "quer falar com gente" });
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(new RespostaLlm(RespostaLlm.StopToolUse,
            [new BlocoUsoFerramentaLlm("esc", "escalar_para_dona", entrada)], 1, 1));

        await CriarServico(new EscalarParaDonaFerramenta(_escalador)).ProcessarTurnoAsync(_empresaId, conversa.Id, Agora);

        await _cloudClient.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default, default);
        await _conversaRepository.Received(1).AddMensagemAsync(Arg.Is<Mensagem>(m =>
            m.Autor == AutorMensagem.Agente && m.ExternoId != null && m.ExternoId.StartsWith("chatsite:")
            && m.Texto == "Vou encaminhar sua mensagem para Michele conferir e responder por aqui."), Arg.Any<CancellationToken>());
    }
}

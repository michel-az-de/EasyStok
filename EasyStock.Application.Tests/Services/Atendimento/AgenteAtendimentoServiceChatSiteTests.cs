using EasyStock.Application.Services.Atendimento;
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

    [Fact]
    public async Task RecusaDoModeloNoChatSite_EncaminhaParaAEquipeSemMostrarOTextoDaRecusa()
    {
        // #3321 (D4): o laço é o mesmo do WhatsApp; no site a recusa também escala e o texto dela não vira mensagem.
        var conversa = Conversa.Abrir(_empresaId, "sessao-3", Agora.AddMinutes(-1), "Visitante", canal: CanalConversa.ChatSite);
        conversa.RegistrarEntrada(Agora.AddMinutes(-1));
        var entradaSite = Mensagem.Entrada(_empresaId, conversa.Id, Agora.AddMinutes(-1), TipoConteudoMensagem.Texto,
            "oi", "chatsite:in3");
        _conversaRepository.ObterComMensagensAsync(_empresaId, conversa.Id, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new ConversaComMensagens(conversa, [entradaSite]));
        _conversaRepository.ObterSituacaoAsync(_empresaId, conversa.Id, Arg.Any<CancellationToken>()).Returns(SituacaoConversa.Automatica);
        _conversaRepository.ObterPorIdComLockAsync(_empresaId, conversa.Id, Arg.Any<CancellationToken>()).Returns(conversa);
        LojaLigada(nomeDona: "Michele");
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>())
            .Returns(new RespostaLlm(RespostaLlm.StopRefusal, [new BlocoTextoLlm("Não posso ajudar com isso.")], 1, 1));

        var resultado = await CriarServico().ProcessarTurnoAsync(_empresaId, conversa.Id, Agora);

        resultado.Escalou.Should().BeTrue();
        await _escalador.Received(1).EscalarAsync(_empresaId, conversa,
            Arg.Is<string>(m => m.StartsWith(GeracaoAgenteAtendimento.MotivoRecusa)), Agora, Arg.Any<CancellationToken>());
        await _conversaRepository.Received().AddMensagemAsync(Arg.Is<Mensagem>(m =>
            m.Autor == AutorMensagem.Agente && m.ExternoId != null && m.ExternoId.StartsWith("chatsite:")), Arg.Any<CancellationToken>());
        await _conversaRepository.DidNotReceive().AddMensagemAsync(Arg.Is<Mensagem>(m =>
            m.Texto != null && m.Texto.Contains("Não posso ajudar")), Arg.Any<CancellationToken>());
        TurnoGravado().Motivo.Should().Be(MotivoDoTurno.RecusaModelo);
    }
}

/// <summary>#2876, seguranca: conversa do site em conferencia de cadastro nao deixa o Assistente ver nada do cliente existente.</summary>
public partial class AgenteAtendimentoServiceTests
{
    [Fact]
    public async Task ConversaDoSiteEmConferencia_AssistenteNaoCarregaClienteNemDossieNemCriaPedido()
    {
        var conversa = Conversa.Abrir(_empresaId, "sessao-2", Agora.AddMinutes(-1), "Visitante", canal: CanalConversa.ChatSite);
        conversa.RegistrarEntrada(Agora.AddMinutes(-1));
        conversa.ExigirConferenciaCliente();
        var entrada = Mensagem.Entrada(_empresaId, conversa.Id, Agora.AddMinutes(-1), TipoConteudoMensagem.Texto,
            "quero ver meus pedidos", "chatsite:in2");
        _conversaRepository.ObterComMensagensAsync(_empresaId, conversa.Id, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new ConversaComMensagens(conversa, [entrada]));
        LojaLigada(nomeDona: "Michele");

        var resultado = await CriarServico().ProcessarTurnoAsync(_empresaId, conversa.Id, Agora);

        resultado.Should().Be(ResultadoTurnoAgente.Ignorado);
        await _llm.DidNotReceiveWithAnyArgs().EnviarAsync(default!, default);
        await _clienteRepository.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
        await _clienteRepository.DidNotReceiveWithAnyArgs().GetByIdWithDetailsAsync(default, default);
        await _conversaRepository.DidNotReceiveWithAnyArgs().AddMensagemAsync(default!, default);
    }
}

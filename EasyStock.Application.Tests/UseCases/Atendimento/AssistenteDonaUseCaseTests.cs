using System.Text.Json;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.UseCases.Atendimento;

/// <summary>Assistente da dona (S47, #1445). O LLM é um fake: nenhuma chamada de rede.</summary>
public class AssistenteDonaUseCaseTests
{
    private static readonly DateTime Agora = new(2026, 9, 29, 15, 0, 0, DateTimeKind.Utc);

    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly IAgenteLlmClient _llm = Substitute.For<IAgenteLlmClient>();
    private readonly IConversaRepository _conversaRepository = Substitute.For<IConversaRepository>();
    private readonly List<RequisicaoLlm> _requisicoes = [];
    private readonly Conversa _conversa;

    public AssistenteDonaUseCaseTests()
    {
        _conversa = Conversa.Abrir(_empresaId, "5511999998888", Agora.AddMinutes(-5), "Maria");
        var historico = new List<Mensagem>
        {
            Mensagem.Entrada(_empresaId, _conversa.Id, Agora.AddMinutes(-5), TipoConteudoMensagem.Texto,
                "o bolo veio amassado, quero meu dinheiro de volta", "wamid.in1")
        };
        _conversaRepository.ObterComMensagensAsync(_empresaId, _conversa.Id, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new ConversaComMensagens(_conversa, historico));

        _llm.When(l => l.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()))
            .Do(ci => _requisicoes.Add(ci.Arg<RequisicaoLlm>()));
        Responder(new RespostaLlm("end_turn", [new BlocoTextoLlm("Pelo CDC, art. 18...")], 120, 40));
    }

    private AssistenteDonaUseCase Criar() =>
        new(_llm, _conversaRepository, NullLogger<AssistenteDonaUseCase>.Instance);

    private void Responder(RespostaLlm resposta) =>
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(resposta);

    private static BlocoUsoFerramentaLlm Uso(string nome, string json) =>
        new($"toolu_{nome}", nome, JsonDocument.Parse(json).RootElement.Clone());

    [Fact]
    public async Task NaoGravaMensagem()
    {
        _llm.Disponivel.Returns(true);

        var resultado = await Criar().ExecuteAsync(
            new PerguntarAssistenteDonaCommand(_empresaId, "Preciso devolver o dinheiro?", _conversa.Id));

        resultado.Resposta.Should().Be("Pelo CDC, art. 18...");
        resultado.Acoes.Should().BeEmpty();
        await _conversaRepository.DidNotReceive().AddMensagemAsync(Arg.Any<Mensagem>(), Arg.Any<CancellationToken>());
        _requisicoes.Should().ContainSingle();
        var textoEnviado = string.Join("\n", _requisicoes[0].Mensagens
            .SelectMany(m => m.Conteudo).OfType<BlocoTextoLlm>().Select(b => b.Texto));
        textoEnviado.Should().Contain("bolo veio amassado").And.Contain("Preciso devolver o dinheiro?");
    }

    [Fact]
    public async Task ComConversa_OfereceSoFerramentasDeProposta()
    {
        _llm.Disponivel.Returns(true);

        await Criar().ExecuteAsync(new PerguntarAssistenteDonaCommand(_empresaId, "manda o cardápio", _conversa.Id));

        // #1445: só propostas; nenhuma ferramenta do agente do WhatsApp (criar pedido, escalar, enviar).
        _requisicoes[0].Ferramentas.Select(f => f.Nome).Should().BeEquivalentTo(
            "propor_envio_cardapio", "propor_nota_interna", "propor_rascunho", "abrir_tela");
    }

    [Fact]
    public async Task SemConversa_NaoOfereceFerramentas()
    {
        _llm.Disponivel.Returns(true);

        await Criar().ExecuteAsync(new PerguntarAssistenteDonaCommand(_empresaId, "Posso cobrar taxa?", null));

        _requisicoes[0].Ferramentas.Should().BeEmpty("sem conversa não há cliente para receber nem anotar");
    }

    [Fact]
    public async Task UsoDeFerramenta_ViraAcaoPropostaSemExecutar()
    {
        _llm.Disponivel.Returns(true);
        Responder(new RespostaLlm(RespostaLlm.StopToolUse,
        [
            new BlocoTextoLlm("Preparei o envio do cardápio."),
            Uso("propor_envio_cardapio", "{}"),
            Uso("propor_nota_interna", """{"texto":"Prefere sem cebola."}"""),
            Uso("propor_rascunho", """{"texto":"Oi Maria! Segue o cardápio."}"""),
            Uso("abrir_tela", """{"tela":"comanda"}"""),
        ], 200, 60));

        var resultado = await Criar().ExecuteAsync(
            new PerguntarAssistenteDonaCommand(_empresaId, "manda o cardápio e anota que ela prefere sem cebola", _conversa.Id));

        resultado.Resposta.Should().Be("Preparei o envio do cardápio.");
        resultado.Acoes.Should().Equal(
            new AcaoPropostaAssistente(AcaoPropostaAssistente.EnviarCardapio, null, null),
            new AcaoPropostaAssistente(AcaoPropostaAssistente.NotaInterna, "Prefere sem cebola.", null),
            new AcaoPropostaAssistente(AcaoPropostaAssistente.Rascunho, "Oi Maria! Segue o cardápio.", null),
            new AcaoPropostaAssistente(AcaoPropostaAssistente.AbrirTela, null, "comanda"));
        // Uma chamada só: a proposta não executa nada no servidor nem pede continuação ao modelo.
        _requisicoes.Should().ContainSingle();
        await _conversaRepository.DidNotReceive().AddMensagemAsync(Arg.Any<Mensagem>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EntradaInvalida_EhDescartada()
    {
        _llm.Disponivel.Returns(true);
        Responder(new RespostaLlm(RespostaLlm.StopToolUse,
        [
            Uso("criar_pedido", """{"itens":[]}"""),
            Uso("propor_nota_interna", """{"texto":"   "}"""),
            Uso("propor_rascunho", "{}"),
            Uso("abrir_tela", """{"tela":"caixa"}"""),
            Uso("propor_envio_cardapio", "{}"),
            Uso("propor_envio_cardapio", "{}"),
        ], 200, 60));

        var resultado = await Criar().ExecuteAsync(
            new PerguntarAssistenteDonaCommand(_empresaId, "faz tudo", _conversa.Id));

        resultado.Acoes.Should().Equal(new AcaoPropostaAssistente(AcaoPropostaAssistente.EnviarCardapio, null, null));
    }

    [Fact]
    public async Task Rascunho_SaiSemTravessao()
    {
        _llm.Disponivel.Returns(true);
        Responder(new RespostaLlm(RespostaLlm.StopToolUse,
            [Uso("propor_rascunho", """{"texto":"Oi Maria — tudo certo, chega hoje."}""")], 100, 20));

        var resultado = await Criar().ExecuteAsync(
            new PerguntarAssistenteDonaCommand(_empresaId, "escreve que chega hoje", _conversa.Id));

        resultado.Acoes.Should().ContainSingle().Which.Texto.Should().Be("Oi Maria, tudo certo, chega hoje.");
    }

    [Fact]
    public void SystemPrompt_CurtoObjetivoEComConfirmacao()
    {
        // #1445: "está sendo muito prolixo"; e ação nunca sai sem o clique da atendente.
        AssistenteDonaUseCase.SystemPrompt.Should()
            .Contain("no máximo 2 frases curtas")
            .And.Contain("sem floreio")
            .And.Contain("só acontece depois que a atendente confirmar")
            .And.NotContain("—").And.NotContain("–");
    }

    [Fact]
    public async Task SemChave503()
    {
        _llm.Disponivel.Returns(false);

        var acao = () => Criar().ExecuteAsync(new PerguntarAssistenteDonaCommand(_empresaId, "Posso cobrar taxa?", null));

        var ex = await acao.Should().ThrowAsync<AssistenteDonaIndisponivelException>();
        ex.Which.Message.Should().Contain("Anthropic:ApiKey");
        await _llm.DidNotReceiveWithAnyArgs().EnviarAsync(default!, default);
    }
}

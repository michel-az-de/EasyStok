using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.UseCases.Atendimento;

/// <summary>Assistente da dona (S47). O LLM é um fake: nenhuma chamada de rede.</summary>
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
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>())
            .Returns(new RespostaLlm("end_turn", [new BlocoTextoLlm("Pelo CDC, art. 18...")], 120, 40));
    }

    private AssistenteDonaUseCase Criar() =>
        new(_llm, _conversaRepository, NullLogger<AssistenteDonaUseCase>.Instance);

    [Fact]
    public async Task NaoGravaMensagem()
    {
        _llm.Disponivel.Returns(true);

        var resultado = await Criar().ExecuteAsync(
            new PerguntarAssistenteDonaCommand(_empresaId, "Preciso devolver o dinheiro?", _conversa.Id));

        resultado.Resposta.Should().Be("Pelo CDC, art. 18...");
        await _conversaRepository.DidNotReceive().AddMensagemAsync(Arg.Any<Mensagem>(), Arg.Any<CancellationToken>());
        _requisicoes.Should().ContainSingle();
        _requisicoes[0].Ferramentas.Should().BeEmpty("o assistente não tem ferramentas de escrita");
        var textoEnviado = string.Join("\n", _requisicoes[0].Mensagens
            .SelectMany(m => m.Conteudo).OfType<BlocoTextoLlm>().Select(b => b.Texto));
        textoEnviado.Should().Contain("bolo veio amassado").And.Contain("Preciso devolver o dinheiro?");
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

using System.Text.Json;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento.Ferramentas;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Application.UseCases.Cliente.Dossie;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.UseCases.Atendimento;

/// <summary>Sugestão do agente para a dona (#1420). O LLM é um fake: nenhuma chamada de rede.</summary>
public class SugerirRespostaAgenteUseCaseTests
{
    private static readonly DateTime Agora = new(2026, 10, 6, 15, 0, 0, DateTimeKind.Utc);

    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly IAgenteLlmClient _llm = Substitute.For<IAgenteLlmClient>();
    private readonly IConversaRepository _conversaRepository = Substitute.For<IConversaRepository>();
    private readonly IConfiguracaoAtendimentoRepository _configuracaoRepository = Substitute.For<IConfiguracaoAtendimentoRepository>();
    private readonly IClienteRepository _clienteRepository = Substitute.For<IClienteRepository>();
    private readonly ICadernoRepository _caderno = Substitute.For<ICadernoRepository>();
    private readonly IFerramentaAgente _consultarCardapio = Ferramenta("consultar_cardapio", """{"itens":[{"nome":"Bolo de cenoura","preco":30}]}""");
    private readonly IFerramentaAgente _criarPedido = Ferramenta("criar_pedido", """{"pedido":"criado"}""");
    private readonly List<RequisicaoLlm> _requisicoes = [];
    private readonly Conversa _conversa;

    public SugerirRespostaAgenteUseCaseTests()
    {
        _conversa = Conversa.Abrir(_empresaId, "5511999998888", Agora.AddMinutes(-10), "Maria");
        _conversa.RegistrarEntrada(Agora.AddMinutes(-2));
        _conversa.Assumir(Agora.AddMinutes(-5), Guid.NewGuid());

        var audio = Mensagem.Entrada(_empresaId, _conversa.Id, Agora.AddMinutes(-2), TipoConteudoMensagem.Audio,
            externoId: "wamid.audio1");
        audio.RegistrarTranscricao("tem bolo de cenoura hoje?");
        var historico = new List<Mensagem>
        {
            Mensagem.Entrada(_empresaId, _conversa.Id, Agora.AddMinutes(-10), TipoConteudoMensagem.Texto, "oi", "wamid.in1"),
            Mensagem.Saida(_empresaId, _conversa.Id, AutorMensagem.Dona, Agora.AddMinutes(-5), TipoConteudoMensagem.Texto,
                "Oi Maria, é a Baba!", "wamid.out1"),
            audio,
        };
        _conversaRepository.ObterComMensagensAsync(_empresaId, _conversa.Id, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new ConversaComMensagens(_conversa, historico));
        _configuracaoRepository.GetByEmpresaIdAsync(_empresaId).Returns((ConfiguracaoAtendimento?)null);

        _llm.Disponivel.Returns(true);
        _llm.When(l => l.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()))
            .Do(ci => _requisicoes.Add(ci.Arg<RequisicaoLlm>()));
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(
            new RespostaLlm(RespostaLlm.StopToolUse,
                [new BlocoUsoFerramentaLlm("toolu_1", "consultar_cardapio", JsonDocument.Parse("{}").RootElement)], 90, 10),
            new RespostaLlm("end_turn", [new BlocoTextoLlm("Temos sim — bolo de cenoura por R$ 30.")], 120, 30));
    }

    private static IFerramentaAgente Ferramenta(string nome, string resultado)
    {
        var ferramenta = Substitute.For<IFerramentaAgente>();
        ferramenta.Nome.Returns(nome);
        ferramenta.Descricao.Returns(nome);
        ferramenta.SchemaJson.Returns("""{"type":"object","properties":{}}""");
        ferramenta.ExecutarAsync(Arg.Any<ContextoTurnoAgente>(), Arg.Any<JsonElement>(), Arg.Any<CancellationToken>())
            .Returns(resultado);
        return ferramenta;
    }

    private SugerirRespostaAgenteUseCase Criar() => new(
        _llm, _conversaRepository, _configuracaoRepository, _clienteRepository,
        [_consultarCardapio, _criarPedido],
        new ObterDossieClienteUseCase(_clienteRepository, Substitute.For<IClienteCrmRepository>(),
            Substitute.For<IHistoricoPedidosClienteQueries>(), Substitute.For<IDomicilioQueries>(), _conversaRepository),
        _caderno, new FakeTimeProvider(new DateTimeOffset(Agora)), NullLogger<SugerirRespostaAgenteUseCase>.Instance);

    private string TextoDasMensagens(int requisicao) => string.Join("\n", _requisicoes[requisicao].Mensagens
        .SelectMany(m => m.Conteudo).OfType<BlocoTextoLlm>().Select(b => b.Texto));

    [Fact]
    public async Task SugereComOContextoDoAgenteSemEnviarNemMudarAConversa()
    {
        var resultado = await Criar().ExecuteAsync(new SugerirRespostaAgenteCommand(_empresaId, _conversa.Id));

        resultado.Texto.Should().Be("Temos sim, bolo de cenoura por R$ 30.");
        resultado.Tokens.Should().Be(250);

        // Mesmo contexto do agente: prompt base, histórico com a transcrição e a fala da dona.
        _requisicoes.Should().HaveCount(2);
        _requisicoes[0].System.Should().Contain("Dossiê desta conversa").And.Contain("MODO SUGESTÃO");
        TextoDasMensagens(0).Should().Contain("[áudio] tem bolo de cenoura hoje?").And.Contain("(dona) Oi Maria, é a Baba!");
        _requisicoes[0].Mensagens[^1].Papel.Should().Be(MensagemLlm.Usuario);

        // Só consulta: a ferramenta que escreve não é oferecida.
        _requisicoes[0].Ferramentas.Select(f => f.Nome).Should().Equal("consultar_cardapio");
        await _criarPedido.DidNotReceiveWithAnyArgs().ExecutarAsync(default!, default, default);

        // Nada sai para o cliente, nada grava e a conversa segue como estava.
        await _conversaRepository.DidNotReceive().AddMensagemAsync(Arg.Any<Mensagem>(), Arg.Any<CancellationToken>());
        _conversa.Situacao.Should().Be(SituacaoConversa.Assumida);
    }

    [Fact]
    public async Task UltimaFalaDaDonaAindaTerminaNoPedidoDeSugestao()
    {
        // A conversa pode terminar com a dona: a API do LLM exige que a última mensagem seja do usuário.
        _conversaRepository.ObterComMensagensAsync(_empresaId, _conversa.Id, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new ConversaComMensagens(_conversa,
            [
                Mensagem.Entrada(_empresaId, _conversa.Id, Agora.AddMinutes(-10), TipoConteudoMensagem.Texto, "oi", "wamid.in1"),
                Mensagem.Saida(_empresaId, _conversa.Id, AutorMensagem.Dona, Agora.AddMinutes(-5), TipoConteudoMensagem.Texto,
                    "Oi Maria!", "wamid.out1"),
            ]));

        await Criar().ExecuteAsync(new SugerirRespostaAgenteCommand(_empresaId, _conversa.Id));

        _requisicoes[0].Mensagens[^1].Papel.Should().Be(MensagemLlm.Usuario);
        _requisicoes[0].Mensagens.Select(m => m.Papel).Should().Equal(MensagemLlm.Usuario, MensagemLlm.Assistente, MensagemLlm.Usuario);
    }

    [Fact]
    public async Task AgenteDesligado503()
    {
        _llm.Disponivel.Returns(false);

        var acao = () => Criar().ExecuteAsync(new SugerirRespostaAgenteCommand(_empresaId, _conversa.Id));

        var ex = await acao.Should().ThrowAsync<AgenteIndisponivelException>();
        ex.Which.Message.Should().Contain("Anthropic:Enabled").And.Contain("Anthropic:ApiKey");
        await _llm.DidNotReceiveWithAnyArgs().EnviarAsync(default!, default);
    }

    [Fact]
    public async Task ConversaInexistente404()
    {
        var acao = () => Criar().ExecuteAsync(new SugerirRespostaAgenteCommand(_empresaId, Guid.NewGuid()));

        await acao.Should().ThrowAsync<ConversaNaoEncontradaException>();
        await _llm.DidNotReceiveWithAnyArgs().EnviarAsync(default!, default);
    }

    [Fact]
    public async Task FalhaDoLlmViraIndisponivel()
    {
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>())
            .Returns<RespostaLlm>(_ => throw new HttpRequestException("timeout"));

        var acao = () => Criar().ExecuteAsync(new SugerirRespostaAgenteCommand(_empresaId, _conversa.Id));

        (await acao.Should().ThrowAsync<AgenteIndisponivelException>())
            .Which.Message.Should().Contain("não conseguiu sugerir");
        await _conversaRepository.DidNotReceive().AddMensagemAsync(Arg.Any<Mensagem>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void InstrucaoSugestao_CurtaEObjetiva()
    {
        // #1445 (homologação 07/10): a sugestão saía prolixa. WhatsApp pede de 1 a 3 frases, sem floreio.
        SugerirRespostaAgenteUseCase.InstrucaoSugestao.Should()
            .Contain("de 1 a 3 frases curtas")
            .And.Contain("sem floreio")
            .And.Contain("sem repetir o que o cliente disse")
            .And.NotContain("—").And.NotContain("–");
    }
}

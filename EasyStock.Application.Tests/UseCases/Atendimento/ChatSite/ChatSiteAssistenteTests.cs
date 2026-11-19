using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento.ChatSite;
using EasyStock.Application.UseCases.FeatureFlags;
using EasyStock.Application.UseCases.Atendimento.Webhook;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using Microsoft.Extensions.Logging.Abstractions;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.Tests.UseCases.Atendimento.ChatSite;

/// <summary>
/// #2876: com o Assistente ligado e o LLM disponível, a conversa do site nasce com o agente (sem escalada) e a
/// mensagem do visitante enfileira o turno. A resposta sai como mensagem do assistente, identificada no contrato.
/// </summary>
public class ChatSiteAssistenteTests
{
    private const string Slug = "casa-da-baba";
    private readonly StorefrontEntity _loja;
    private readonly IStorefrontRepository _lojas = Substitute.For<IStorefrontRepository>();
    private readonly ITenantFeatureFlagRepository _flags = Substitute.For<ITenantFeatureFlagRepository>();
    private readonly ITenantContextAccessor _tenant = Substitute.For<ITenantContextAccessor>();
    private readonly ISessaoChatSiteRepository _sessoes = Substitute.For<ISessaoChatSiteRepository>();
    private readonly IConversaRepository _conversas = Substitute.For<IConversaRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IConfiguracaoAtendimentoRepository _configuracoes = Substitute.For<IConfiguracaoAtendimentoRepository>();
    private readonly IAgenteLlmClient _llm = Substitute.For<IAgenteLlmClient>();
    private readonly IQueueService _fila = Substitute.For<IQueueService>();
    private readonly List<Conversa> _criadas = [];
    private readonly AcessoChatSite _acesso;

    public ChatSiteAssistenteTests()
    {
        _loja = StorefrontEntity.Criar(empresaId: Guid.NewGuid(), slug: Slug, tituloPublico: "Casa da Baba", pedidoMinimoEntrega: 0m);
        _loja.Ativar();
        _lojas.GetBySlugAsync(Slug, Arg.Any<CancellationToken>()).Returns(_loja);
        _flags.ListarAtivasAsync(_loja.EmpresaId, Arg.Any<CancellationToken>())
            .Returns([FeatureCatalogo.ModuloAtendimento, FeatureCatalogo.CanalChatSite]);
        _acesso = new AcessoChatSite(_lojas, _flags, _tenant, _sessoes);
        _sessoes.ObterSnapshotPorTokenHashAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(c => _sessoes.ObterPorTokenHashAsync(c.Arg<Guid>(), c.Arg<string>(), c.Arg<CancellationToken>()));
        _conversas.When(x => x.AddAsync(Arg.Any<Conversa>(), Arg.Any<CancellationToken>())).Do(ci => _criadas.Add(ci.Arg<Conversa>()));
        _conversas.ObterPorIdComLockAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(async ci => await _conversas.ObterPorIdAsync(ci.ArgAt<Guid>(0), ci.ArgAt<Guid>(1), ci.ArgAt<CancellationToken>(2))
                ?? _criadas.FirstOrDefault(c => c.Id == ci.ArgAt<Guid>(1)));
        _uow.ExecuteInTransactionSemRetryAsync(Arg.Any<Func<CancellationToken, Task<Conversa>>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<Func<CancellationToken, Task<Conversa>>>()(ci.Arg<CancellationToken>()));
        _uow.ExecuteInTransactionSemRetryAsync(Arg.Any<Func<CancellationToken, Task<Mensagem>>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<Func<CancellationToken, Task<Mensagem>>>()(ci.Arg<CancellationToken>()));
    }

    private string SessaoValida()
    {
        var token = AcessoChatSite.NovoToken();
        var sessao = SessaoChatSite.Abrir(_loja.EmpresaId, _loja.Id, AcessoChatSite.HashDoToken(token), DateTime.UtcNow);
        _sessoes.ObterPorTokenHashAsync(_loja.EmpresaId, AcessoChatSite.HashDoToken(token), Arg.Any<CancellationToken>()).Returns(sessao);
        return token;
    }

    private void LigarAssistente()
    {
        var configuracao = ConfiguracaoAtendimento.CriarPadrao(_loja.EmpresaId);
        configuracao.Ativo = true;
        _configuracoes.GetOrDefaultAsync(_loja.EmpresaId).Returns(configuracao);
        _llm.Disponivel.Returns(true);
    }

    private EnviarMensagemVisitanteUseCase Enviar() => new(
        _acesso, _conversas, Substitute.For<IOperacaoEventPublisher>(), _uow, NullLogger<EnviarMensagemVisitanteUseCase>.Instance,
        new ConversaChatSiteService(_conversas, _uow, _sessoes, configuracoes: _configuracoes, llm: _llm), _fila);

    [Fact]
    public async Task ComAssistenteLigado_NovaConversaDoSiteNaoVaiParaFilaHumana()
    {
        LigarAssistente();
        var token = SessaoValida();

        await Enviar().ExecuteAsync(Slug, token, "oi");

        _criadas.Should().ContainSingle().Which.Situacao.Should().Be(SituacaoConversa.Automatica);
    }

    [Fact]
    public async Task ComAssistenteLigado_EnfileiraTurnoESemConfirmacaoAutomatica()
    {
        LigarAssistente();
        var token = SessaoValida();

        await Enviar().ExecuteAsync(Slug, token, "oi");

        await _fila.Received(1).EnqueueAsync(FilaAtendimentoNomes.TurnoAgente, Arg.Any<ProcessarTurnoAgenteJob>());
        await _conversas.DidNotReceive().AddMensagemAsync(Arg.Is<Mensagem>(m => m.Autor == AutorMensagem.Sistema), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(AutorMensagem.Agente, "assistente")]
    [InlineData(AutorMensagem.Dona, "equipe")]
    [InlineData(AutorMensagem.Atendente, "equipe")]
    [InlineData(AutorMensagem.Sistema, "equipe")]
    public void MensagemDeSaida_IdentificaQuemRespondeu(AutorMensagem autor, string esperado)
    {
        var mensagem = Mensagem.Saida(_loja.EmpresaId, Guid.NewGuid(), autor, DateTime.UtcNow, TipoConteudoMensagem.Texto, "oi", "chatsite:x");

        MensagemChatSiteResult.De(mensagem).Autor.Should().Be(esperado);
    }

    [Fact]
    public void MensagemDoVisitante_TemAutorVisitante()
    {
        var mensagem = Mensagem.Entrada(_loja.EmpresaId, Guid.NewGuid(), DateTime.UtcNow, TipoConteudoMensagem.Texto, "oi", "x");

        MensagemChatSiteResult.De(mensagem).Autor.Should().Be("visitante");
    }

    [Fact]
    public async Task Stream_EntregaAoWidgetAsSaidasGravadasPeloAgente()
    {
        var token = SessaoValida();
        var conversaId = Guid.NewGuid();
        var sessao = await _sessoes.ObterPorTokenHashAsync(_loja.EmpresaId, AcessoChatSite.HashDoToken(token), CancellationToken.None);
        sessao!.VincularConversa(conversaId);
        var resposta = Mensagem.Saida(_loja.EmpresaId, conversaId, AutorMensagem.Agente, DateTime.UtcNow, TipoConteudoMensagem.Texto,
            "Oi! Posso ajudar?", CanalChatSite.PrefixoIdExterno + Guid.NewGuid().ToString("N"));
        var nota = Mensagem.Saida(_loja.EmpresaId, conversaId, AutorMensagem.Sistema, DateTime.UtcNow, TipoConteudoMensagem.Texto, "nota interna");
        _conversas.ListarMensagensDepoisAsync(_loja.EmpresaId, conversaId, Arg.Any<DateTime?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([resposta, nota]);

        var entregues = await new ListarMensagensChatSiteUseCase(_acesso, _sessoes, _conversas)
            .ListarParaStreamAsync(_loja.EmpresaId, AcessoChatSite.HashDoToken(token), null);

        entregues.Should().ContainSingle().Which.Should().Match<MensagemChatSiteResult>(m => m.Autor == "assistente" && !m.DoVisitante);
    }
}

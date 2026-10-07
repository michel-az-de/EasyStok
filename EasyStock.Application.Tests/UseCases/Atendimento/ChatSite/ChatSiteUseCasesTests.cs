using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento.ChatSite;
using EasyStock.Application.UseCases.FeatureFlags;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using Microsoft.Extensions.Logging.Abstractions;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.Tests.UseCases.Atendimento.ChatSite;

/// <summary>
/// Chat do site (S36): sessão anônima presa à loja, token guardado só como hash, conversa do canal
/// ChatSite na fila humana, e o visitante só vê a mensagem dele e a da loja.
/// </summary>
public class ChatSiteUseCasesTests
{
    private const string Slug = "casa-da-baba";
    private readonly StorefrontEntity _loja;
    private readonly IStorefrontRepository _lojas = Substitute.For<IStorefrontRepository>();
    private readonly ITenantFeatureFlagRepository _flags = Substitute.For<ITenantFeatureFlagRepository>();
    private readonly ITenantContextAccessor _tenant = Substitute.For<ITenantContextAccessor>();
    private readonly ISessaoChatSiteRepository _sessoes = Substitute.For<ISessaoChatSiteRepository>();
    private readonly IConversaRepository _conversas = Substitute.For<IConversaRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly AcessoChatSite _acesso;

    public ChatSiteUseCasesTests()
    {
        _loja = StorefrontEntity.Criar(empresaId: Guid.NewGuid(), slug: Slug, tituloPublico: "Casa da Babá", pedidoMinimoEntrega: 0m);
        _loja.Ativar();
        _lojas.GetBySlugAsync(Slug, Arg.Any<CancellationToken>()).Returns(_loja);
        _flags.ListarAtivasAsync(_loja.EmpresaId, Arg.Any<CancellationToken>())
            .Returns([FeatureCatalogo.ModuloAtendimento, FeatureCatalogo.CanalChatSite]);
        _acesso = new AcessoChatSite(_lojas, _flags, _tenant, _sessoes);
        _uow.ExecuteInTransactionSemRetryAsync(Arg.Any<Func<CancellationToken, Task<Conversa>>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<Func<CancellationToken, Task<Conversa>>>()(ci.Arg<CancellationToken>()));
    }

    private (SessaoChatSite Sessao, string Token) SessaoValida(Guid? storefrontId = null, DateTime? abertaEm = null)
    {
        var token = AcessoChatSite.NovoToken();
        var sessao = SessaoChatSite.Abrir(_loja.EmpresaId, storefrontId ?? _loja.Id, AcessoChatSite.HashDoToken(token), abertaEm ?? DateTime.UtcNow);
        _sessoes.ObterPorTokenHashAsync(_loja.EmpresaId, AcessoChatSite.HashDoToken(token), Arg.Any<CancellationToken>()).Returns(sessao);
        return (sessao, token);
    }

    private EnviarMensagemVisitanteUseCase Enviar() => new(
        _acesso, _conversas, Substitute.For<IOperacaoEventPublisher>(), _uow, NullLogger<EnviarMensagemVisitanteUseCase>.Instance, new ConversaChatSiteService(_conversas, _uow));

    [Fact]
    public async Task AbrirSessao_DevolveTokenEGuardaSoOHash()
    {
        var aberta = await new AbrirSessaoChatSiteUseCase(_acesso, _sessoes, _uow).ExecuteAsync(Slug);

        aberta.Token.Should().HaveLength(43, "32 bytes em base64url sem padding");
        await _sessoes.Received(1).AddAsync(
            Arg.Is<SessaoChatSite>(s => s.TokenHash == AcessoChatSite.HashDoToken(aberta.Token) && s.TokenHash != aberta.Token
                && s.StorefrontId == _loja.Id && s.EmpresaId == _loja.EmpresaId),
            Arg.Any<CancellationToken>());
        _tenant.Received(1).SetCurrentTenant(_loja.EmpresaId);
        await _uow.Received(1).CommitAsync();
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task FlagDesligada_Indisponivel(bool modulo, bool chat)
    {
        var ativas = new List<string>();
        if (modulo) ativas.Add(FeatureCatalogo.ModuloAtendimento);
        if (chat) ativas.Add(FeatureCatalogo.CanalChatSite);
        _flags.ListarAtivasAsync(_loja.EmpresaId, Arg.Any<CancellationToken>()).Returns(ativas);

        var act = () => new AbrirSessaoChatSiteUseCase(_acesso, _sessoes, _uow).ExecuteAsync(Slug);

        await act.Should().ThrowAsync<ChatSiteIndisponivelException>();
        await _sessoes.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task LojaInexistente_Indisponivel()
    {
        var act = () => new AbrirSessaoChatSiteUseCase(_acesso, _sessoes, _uow).ExecuteAsync("outra");
        await act.Should().ThrowAsync<ChatSiteIndisponivelException>();
    }

    [Fact]
    public async Task PrimeiraMensagem_AbreConversaChatSiteNaFilaHumanaSemResponsavel()
    {
        var (sessao, token) = SessaoValida();
        Conversa? criada = null;
        await _conversas.AddAsync(Arg.Do<Conversa>(c => criada = c), Arg.Any<CancellationToken>());

        var resultado = await Enviar().ExecuteAsync(Slug, token, "  Oi, vocês entregam hoje?  ");

        criada.Should().NotBeNull();
        criada!.Canal.Should().Be(CanalConversa.ChatSite);
        criada.ContatoIdExterno.Should().Be(sessao.ContatoIdExterno);
        criada.Situacao.Should().Be(SituacaoConversa.Assumida);
        criada.AssumidaPorUsuarioId.Should().BeNull();
        criada.NaoLidas.Should().Be(1);
        sessao.ConversaId.Should().Be(criada.Id);
        resultado.DoVisitante.Should().BeTrue();
        resultado.Texto.Should().Be("Oi, vocês entregam hoje?");
        await _conversas.Received(1).AddMensagemAsync(
            Arg.Is<Mensagem>(m => m.ConversaId == criada.Id && m.Direcao == DirecaoMensagem.Entrada), Arg.Any<CancellationToken>());
        await _uow.Received(2).CommitAsync();
    }

    [Fact]
    public async Task SegundaMensagem_ReaproveitaAConversa()
    {
        var (sessao, token) = SessaoValida();
        var conversa = Conversa.Abrir(_loja.EmpresaId, sessao.ContatoIdExterno, DateTime.UtcNow, canal: CanalConversa.ChatSite);
        sessao.VincularConversa(conversa.Id);
        _conversas.ObterPorIdAsync(_loja.EmpresaId, conversa.Id, Arg.Any<CancellationToken>()).Returns(conversa);

        await Enviar().ExecuteAsync(Slug, token, "mais uma");

        await _conversas.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        conversa.NaoLidas.Should().Be(1);
    }

    [Fact]
    public async Task TokenInventadoVencidoOuDeOutraLoja_Invalida()
    {
        var (_, deOutraLoja) = SessaoValida(storefrontId: Guid.NewGuid());
        var (_, vencido) = SessaoValida(abertaEm: DateTime.UtcNow.AddHours(-25));

        foreach (var token in new[] { "inventado", deOutraLoja, vencido, null })
        {
            var act = () => Enviar().ExecuteAsync(Slug, token, "oi");
            await act.Should().ThrowAsync<SessaoChatSiteInvalidaException>();
        }

        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task Listar_MostraSoAMensagemDoVisitanteEADaLoja()
    {
        var (sessao, token) = SessaoValida();
        var conversaId = Guid.NewGuid();
        sessao.VincularConversa(conversaId);
        var agora = DateTime.UtcNow;
        var doVisitante = Mensagem.Entrada(_loja.EmpresaId, conversaId, agora, TipoConteudoMensagem.Texto, "oi");
        var daLoja = Mensagem.Saida(_loja.EmpresaId, conversaId, AutorMensagem.Dona, agora.AddSeconds(1), TipoConteudoMensagem.Texto, "olá!", "chatsite:1");
        var notaInterna = Mensagem.Saida(_loja.EmpresaId, conversaId, AutorMensagem.Sistema, agora.AddSeconds(2), TipoConteudoMensagem.Texto, "conversa transferida");
        _conversas.ListarMensagensDepoisAsync(_loja.EmpresaId, conversaId, null, ListarMensagensChatSiteUseCase.Limite, Arg.Any<CancellationToken>())
            .Returns([doVisitante, daLoja, notaInterna]);

        var lista = await new ListarMensagensChatSiteUseCase(_acesso, _sessoes, _conversas).ExecuteAsync(Slug, token, null);

        lista.Select(m => m.Texto).Should().Equal("oi", "olá!");
        lista[0].DoVisitante.Should().BeTrue();
        lista[1].DoVisitante.Should().BeFalse();
    }

    [Fact]
    public async Task CanalChatSite_DevolveIdExternoESoTexto()
    {
        var canal = new CanalChatSite();

        (await canal.EnviarTextoAsync("sessao", "olá")).Should().StartWith(CanalChatSite.PrefixoIdExterno);
        canal.Canal.Should().Be(CanalConversa.ChatSite);
        var imagem = () => canal.EnviarImagemAsync("sessao", "https://x/y.png");
        await imagem.Should().ThrowAsync<NotSupportedException>();
    }
    [Fact]
    public async Task Stream_UsaSnapshotAtualENaoSessaoRastreadaAntesDoLogout()
    {
        var (sessao, token) = SessaoValida();
        _sessoes.ObterSnapshotPorTokenHashAsync(_loja.EmpresaId, sessao.TokenHash, Arg.Any<CancellationToken>())
            .Returns((SessaoChatSite?)null);
        var sut = new ListarMensagensChatSiteUseCase(_acesso, _sessoes, _conversas);
        await sut.Invoking(x => x.ListarParaStreamAsync(_loja.EmpresaId, AcessoChatSite.HashDoToken(token), null))
            .Should().ThrowAsync<SessaoChatSiteInvalidaException>();
        await _conversas.DidNotReceiveWithAnyArgs().ListarMensagensDepoisAsync(default, default, default, default, default);
    }

    // ── #1430: formulário antes do chat ──

    private IdentificarVisitanteChatSiteUseCase Identificar() => new(_acesso, _conversas, _uow);

    private static IdentificacaoVisitanteInput Formulario(string? email = "Maria@Exemplo.com", bool aceite = true) =>
        new("Maria Souza", "(11) 98765-4321", email, aceite);

    [Fact]
    public async Task Identificar_GravaNaSessaoSemProcurarCliente()
    {
        var (sessao, token) = SessaoValida();

        var r = await Identificar().ExecuteAsync(Slug, token, Formulario());

        r.Should().BeEquivalentTo(new { Nome = "Maria Souza", Telefone = "+5511987654321", Email = "maria@exemplo.com" });
        sessao.ContatoInformado!.Telefone.Should().Be("+5511987654321");
        await _uow.Received(1).CommitAsync();
        await _conversas.DidNotReceiveWithAnyArgs().ObterPorIdAsync(default, default, default);
    }

    [Fact]
    public async Task ConversaNasceDepoisDoFormulario_ComNomeEContatoInformado()
    {
        var (sessao, token) = SessaoValida();
        await Identificar().ExecuteAsync(Slug, token, Formulario());
        Conversa? criada = null;
        await _conversas.AddAsync(Arg.Do<Conversa>(c => criada = c), Arg.Any<CancellationToken>());

        await Enviar().ExecuteAsync(Slug, token, "Oi");

        criada!.ContatoNome.Should().Be("Maria Souza");
        criada.ContatoTelefoneInformado.Should().Be("+5511987654321");
        criada.ContatoEmailInformado.Should().Be("maria@exemplo.com");
        criada.ContatoInformadoEm.Should().Be(sessao.VisitanteInformadoEm);
        criada.ClienteId.Should().BeNull("o telefone digitado não liga a conversa a cliente nenhum");
    }

    [Fact]
    public async Task SemFormulario_ConversaNasceComoVisitanteDoSite()
    {
        var (_, token) = SessaoValida();
        Conversa? criada = null;
        await _conversas.AddAsync(Arg.Do<Conversa>(c => criada = c), Arg.Any<CancellationToken>());

        await Enviar().ExecuteAsync(Slug, token, "Oi");

        criada!.ContatoNome.Should().Be(ConversaChatSiteService.NomeSemFormulario);
        criada.ContatoInformado.Should().BeNull();
    }

    [Fact]
    public async Task Identificar_ComConversaAberta_AtualizaNomeEContato()
    {
        var (sessao, token) = SessaoValida();
        var conversa = Conversa.Abrir(_loja.EmpresaId, sessao.ContatoIdExterno, DateTime.UtcNow, "Visitante do site", canal: CanalConversa.ChatSite);
        sessao.VincularConversa(conversa.Id);
        _conversas.ObterPorIdAsync(_loja.EmpresaId, conversa.Id, Arg.Any<CancellationToken>()).Returns(conversa);

        await Identificar().ExecuteAsync(Slug, token, Formulario(email: null));

        conversa.ContatoNome.Should().Be("Maria Souza");
        conversa.ContatoTelefoneInformado.Should().Be("+5511987654321");
        conversa.ContatoEmailInformado.Should().BeNull();
    }

    [Theory]
    [InlineData(false, "(11) 98765-4321", null)]
    [InlineData(true, "123", null)]
    [InlineData(true, "(11) 98765-4321", "sem-arroba")]
    public async Task Identificar_DadoInvalidoOuSemAceite_ValidacaoSemGravar(bool aceite, string telefone, string? email)
    {
        var (sessao, token) = SessaoValida();

        var act = () => Identificar().ExecuteAsync(Slug, token, new IdentificacaoVisitanteInput("Maria", telefone, email, aceite));

        await act.Should().ThrowAsync<UseCaseValidationException>();
        sessao.ContatoInformado.Should().BeNull();
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task Identificar_SemTokenValido_Invalida()
    {
        var act = () => Identificar().ExecuteAsync(Slug, "token-inventado", Formulario());

        await act.Should().ThrowAsync<SessaoChatSiteInvalidaException>();
    }
}

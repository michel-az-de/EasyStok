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
using EasyStock.Domain.Sales;
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
    private readonly IPedidoRepository _pedidos = Substitute.For<IPedidoRepository>();
    private readonly AcessoChatSite _acesso;

    public ChatSiteUseCasesTests()
    {
        _loja = StorefrontEntity.Criar(empresaId: Guid.NewGuid(), slug: Slug, tituloPublico: "Casa da Babá", pedidoMinimoEntrega: 0m);
        _loja.Ativar();
        _lojas.GetBySlugAsync(Slug, Arg.Any<CancellationToken>()).Returns(_loja);
        _flags.ListarAtivasAsync(_loja.EmpresaId, Arg.Any<CancellationToken>())
            .Returns([FeatureCatalogo.ModuloAtendimento, FeatureCatalogo.CanalChatSite]);
        _acesso = new AcessoChatSite(_lojas, _flags, _tenant, _sessoes);
        // #1797: a mensagem relê a conversa travada; no dublê, a já cadastrada ou a recém-criada.
        var criadas = new List<Conversa>();
        _conversas.When(x => x.AddAsync(Arg.Any<Conversa>(), Arg.Any<CancellationToken>())).Do(ci => criadas.Add(ci.Arg<Conversa>()));
        _conversas.ObterPorIdComLockAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(async ci => await _conversas.ObterPorIdAsync(ci.ArgAt<Guid>(0), ci.ArgAt<Guid>(1), ci.ArgAt<CancellationToken>(2))
                ?? criadas.FirstOrDefault(c => c.Id == ci.ArgAt<Guid>(1)));
        _uow.ExecuteInTransactionSemRetryAsync(Arg.Any<Func<CancellationToken, Task<Conversa>>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<Func<CancellationToken, Task<Conversa>>>()(ci.Arg<CancellationToken>()));
        _uow.ExecuteInTransactionSemRetryAsync(Arg.Any<Func<CancellationToken, Task<Mensagem>>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<Func<CancellationToken, Task<Mensagem>>>()(ci.Arg<CancellationToken>()));
    }

    private (SessaoChatSite Sessao, string Token) SessaoValida(Guid? storefrontId = null, DateTime? abertaEm = null)
    {
        var token = AcessoChatSite.NovoToken();
        var sessao = SessaoChatSite.Abrir(_loja.EmpresaId, storefrontId ?? _loja.Id, AcessoChatSite.HashDoToken(token), abertaEm ?? DateTime.UtcNow);
        _sessoes.ObterPorTokenHashAsync(_loja.EmpresaId, AcessoChatSite.HashDoToken(token), Arg.Any<CancellationToken>()).Returns(sessao);
        return (sessao, token);
    }

    private EnviarMensagemVisitanteUseCase Enviar() => new(
        _acesso, _conversas, Substitute.For<IOperacaoEventPublisher>(), _uow, NullLogger<EnviarMensagemVisitanteUseCase>.Instance, new ConversaChatSiteService(_conversas, _uow, _pedidos));

    /// <summary>
    /// #1918: o visitante escreve depois do encerramento com o pedido feito pela página ainda em andamento. A conversa
    /// da sessão volta à fila humana com o pedido (como no WhatsApp, #1765-5), em vez de nascer "Visitante do site" sem ele.
    /// </summary>
    [Fact]
    public async Task MensagemDepoisDoEncerramentoComPedidoEmAndamento_ReabreAConversaDaSessao()
    {
        var pedido = Pedido.Criar(_loja.EmpresaId, origem: "site");
        pedido.Status = StatusPedidoMapper.Preparando;
        var (encerrada, token) = SessaoComConversaEncerrada(pedido);

        await Enviar().ExecuteAsync(Slug, token, "Cadê meu pedido?");

        await _conversas.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        encerrada.EstaAberta.Should().BeTrue();
        encerrada.PedidoEmAndamentoId.Should().Be(pedido.Id);
        await _conversas.Received().AddMensagemAsync(
            Arg.Is<Mensagem>(m => m.ConversaId == encerrada.Id && m.Texto == "Cadê meu pedido?"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MensagemDepoisDoEncerramentoComPedidoEntregue_AbreOutraConversa()
    {
        var pedido = Pedido.Criar(_loja.EmpresaId, origem: "site");
        pedido.Status = StatusPedidoMapper.Entregue;
        var (encerrada, token) = SessaoComConversaEncerrada(pedido);

        await Enviar().ExecuteAsync(Slug, token, "Oi de novo");

        await _conversas.Received(1).AddAsync(Arg.Any<Conversa>(), Arg.Any<CancellationToken>());
        encerrada.EstaAberta.Should().BeFalse();
    }

    private (Conversa Encerrada, string Token) SessaoComConversaEncerrada(Pedido pedido)
    {
        var (sessao, token) = SessaoValida();
        var encerrada = Conversa.Abrir(_loja.EmpresaId, sessao.ContatoIdExterno, DateTime.UtcNow.AddHours(-3),
            ConversaChatSiteService.NomeSemFormulario, canal: CanalConversa.ChatSite);
        encerrada.DefinirPedidoEmAndamento(pedido.Id);
        encerrada.Encerrar(DateTime.UtcNow.AddHours(-1));
        sessao.VincularConversa(encerrada.Id);
        _conversas.ObterPorIdAsync(_loja.EmpresaId, encerrada.Id, Arg.Any<CancellationToken>()).Returns(encerrada);
        _pedidos.GetByIdAsync(_loja.EmpresaId, pedido.Id).Returns(pedido);
        return (encerrada, token);
    }

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
        criada.MotivoEscalada.Should().Be(ConversaChatSiteService.MotivoFilaHumana);
        criada.NaoLidas.Should().Be(1);
        sessao.ConversaId.Should().Be(criada.Id);
        resultado.DoVisitante.Should().BeTrue();
        resultado.Texto.Should().Be("Oi, vocês entregam hoje?");
        await _conversas.Received(1).AddMensagemAsync(
            Arg.Is<Mensagem>(m => m.ConversaId == criada.Id && m.Direcao == DirecaoMensagem.Entrada), Arg.Any<CancellationToken>());
        await _uow.Received(2).CommitAsync();
    }

    // #1991-4: entrada e "Recebemos sua mensagem" saíam com o mesmo enviadaEm e a ordem do chat variava. A resposta
    // fica ao menos 1 µs depois (a precisão do timestamp do Postgres), então nunca empata com a entrada.
    [Fact]
    public async Task PrimeiraMensagem_ConfirmacaoDeRecebimentoFicaDepoisDaEntrada()
    {
        var (_, token) = SessaoValida();
        var gravadas = new List<Mensagem>();
        await _conversas.AddMensagemAsync(Arg.Do<Mensagem>(gravadas.Add), Arg.Any<CancellationToken>());

        await Enviar().ExecuteAsync(Slug, token, "Oi");

        var entrada = gravadas.Single(m => m.Direcao == DirecaoMensagem.Entrada);
        var confirmacao = gravadas.Single(m => m.Direcao == DirecaoMensagem.Saida);
        (confirmacao.EnviadaEm - entrada.EnviadaEm).Should().BeGreaterThanOrEqualTo(TimeSpan.FromMicroseconds(1));
    }

    [Fact]
    public async Task SegundaMensagem_ReaproveitaAConversa()
    {
        var (sessao, token) = SessaoValida();
        var conversa = Conversa.Abrir(_loja.EmpresaId, sessao.ContatoIdExterno, DateTime.UtcNow, canal: CanalConversa.ChatSite);
        sessao.VincularConversa(conversa.Id);
        _conversas.ObterPorIdAsync(_loja.EmpresaId, conversa.Id, Arg.Any<CancellationToken>()).Returns(conversa);
        var confirmacao = Mensagem.Saida(_loja.EmpresaId, conversa.Id, AutorMensagem.Sistema, DateTime.UtcNow,
            TipoConteudoMensagem.Texto, "Recebemos sua mensagem.", EnviarMensagemVisitanteUseCase.PrefixoConfirmacaoRecebimento + conversa.Id.ToString("N"));
        _conversas.ObterMensagemPorExternoIdAsync(_loja.EmpresaId, confirmacao.ExternoId!, Arg.Any<CancellationToken>()).Returns(confirmacao);

        await Enviar().ExecuteAsync(Slug, token, "mais uma");

        await _conversas.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        conversa.NaoLidas.Should().Be(1);
        await _conversas.DidNotReceive().AddMensagemAsync(Arg.Is<Mensagem>(m => m.Direcao == DirecaoMensagem.Saida), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("Conversa reaberta para atendimento.", false)]
    [InlineData("Motivo real preservado", false)]
    [InlineData(null, true)]
    public async Task Entrada_PreservaMotivoRealEResponsavel(string? motivo, bool assumida)
    {
        var (sessao, token) = SessaoValida();
        var agora = DateTime.UtcNow;
        var conversa = Conversa.Abrir(_loja.EmpresaId, sessao.ContatoIdExterno, agora, canal: CanalConversa.ChatSite);
        var dona = assumida ? Guid.NewGuid() : (Guid?)null;
        if (assumida) conversa.Assumir(agora, dona); else conversa.Escalar(agora, motivo);
        sessao.VincularConversa(conversa.Id);
        _conversas.ObterPorIdAsync(_loja.EmpresaId, conversa.Id, Arg.Any<CancellationToken>()).Returns(conversa);

        await Enviar().ExecuteAsync(Slug, token, "Mais uma dúvida");

        conversa.MotivoEscalada.Should().Be(motivo);
        conversa.AssumidaPorUsuarioId.Should().Be(dona);
        conversa.Situacao.Should().Be(SituacaoConversa.Assumida);
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
    public async Task CanalChatSite_DevolveIdExternoParaTextoEFoto()
    {
        var canal = new CanalChatSite();

        (await canal.EnviarTextoAsync("sessao", "olá")).Should().StartWith(CanalChatSite.PrefixoIdExterno);
        canal.Canal.Should().Be(CanalConversa.ChatSite);
        // #1448: a foto da loja é a mensagem gravada (o visitante busca a mídia pela sessão). O que ainda
        // segura o envio é a capacidade do canal, desligada até o widget do site desenhar imagem.
        (await canal.EnviarImagemAsync("sessao", "https://x/y.png")).Should().StartWith(CanalChatSite.PrefixoIdExterno);
        var botoes = () => canal.EnviarBotoesAsync("sessao", "x", [("a", "A")]);
        await botoes.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task MensagemDoChat_DizOTipoESeTemMidia()
    {
        var (sessao, token) = SessaoValida();
        var conversaId = Guid.NewGuid();
        sessao.VincularConversa(conversaId);
        var foto = Mensagem.Saida(_loja.EmpresaId, conversaId, AutorMensagem.Dona, DateTime.UtcNow, TipoConteudoMensagem.Imagem, null, "chatsite:1");
        foto.AnexarMidia("atendimento/e/c/f.jpg", "image/jpeg");
        var texto = Mensagem.Saida(_loja.EmpresaId, conversaId, AutorMensagem.Dona, DateTime.UtcNow, TipoConteudoMensagem.Texto, "oi", "chatsite:2");
        _conversas.ListarMensagensDepoisAsync(_loja.EmpresaId, conversaId, null, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([foto, texto]);

        var lista = await new ListarMensagensChatSiteUseCase(_acesso, _sessoes, _conversas).ExecuteAsync(Slug, token, null);

        lista.Select(m => (m.Tipo, m.TemMidia)).Should().Equal(("imagem", true), ("texto", false));
    }

    [Fact]
    public void ChatSite_SegueSemAceitarFotoAteOWidgetDesenhar()
    {
        // #1448: dependência do widget (repo casa-da-baba). Ligar só quando ele desenhar `tipo: imagem`.
        EasyStock.Domain.ValueObjects.CapacidadesCanal.Para(CanalConversa.ChatSite).AceitaImagem.Should().BeFalse();
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

    private readonly IOperacaoEventPublisher _operacao = Substitute.For<IOperacaoEventPublisher>();

    private IdentificarVisitanteChatSiteUseCase Identificar() => new(_acesso, _conversas, _uow, _operacao);

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
        // #1643-5: o console troca o nome sem esperar a próxima sincronização; o aviso sai depois do commit.
        await _operacao.Received(1).PublicarAsync(EventosOperacao.ConversaAtualizada, _loja.EmpresaId,
            Arg.Is<ConversaAtualizadaOperacao>(e => e.ConversaId == conversa.Id), Arg.Any<CancellationToken>());
        Received.InOrder(() =>
        {
            _uow.CommitAsync();
            _operacao.PublicarAsync(EventosOperacao.ConversaAtualizada, _loja.EmpresaId, Arg.Any<object>(), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Identificar_SemConversaAberta_NaoAvisaOConsole()
    {
        var (_, token) = SessaoValida();

        await Identificar().ExecuteAsync(Slug, token, Formulario());

        await _operacao.DidNotReceiveWithAnyArgs().PublicarAsync(default!, default, default!, default);
    }

    // #1643-4: sessão inválida é a primeira resposta, antes de olhar o corpo (o aceite da #1430 pedia 404/403, não 400).
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Identificar_CorpoInvalidoComTokenInventado_DizSessaoInvalida(bool semCorpo)
    {
        var act = () => Identificar().ExecuteAsync(Slug, "token-inventado",
            semCorpo ? null : new IdentificacaoVisitanteInput("Maria", "123", null, true));

        await act.Should().ThrowAsync<SessaoChatSiteInvalidaException>();
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

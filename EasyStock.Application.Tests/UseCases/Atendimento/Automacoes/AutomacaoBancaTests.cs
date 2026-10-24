using System.Text.Json;
using EasyStock.Application.Events.Atendimento;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Automacoes;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Domain.Integration;
using EasyStock.Domain.Sales;
using EasyStock.TestHelpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ClienteEntity = EasyStock.Domain.Entities.Cliente;
using PedidoEntity = EasyStock.Domain.Entities.Pedido;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Automacoes;

/// <summary>
/// Banca de 23/10 (#2004): a automática que falha deixa rastro, o envio incerto não vira "não saiu", o pagamento
/// confirmado não chega depois da entrega, o pós-entrega vai pela conversa do pedido e o número do pedido que o
/// cliente lê é o mesmo no aviso e na automática.
/// </summary>
public class AutomacaoBancaTests
{
    private static readonly DateTime Agora = new(2026, 9, 30, 15, 0, 0, DateTimeKind.Utc);

    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly ClienteEntity _cliente;
    private readonly Conversa _conversa;
    private readonly IRegraAutomaticaRepository _regras = Substitute.For<IRegraAutomaticaRepository>();
    private readonly IConversaRepository _conversas = Substitute.For<IConversaRepository>();
    private readonly IClienteRepository _clientes = Substitute.For<IClienteRepository>();
    private readonly IPedidoRepository _pedidos = Substitute.For<IPedidoRepository>();
    private readonly IExpedienteLojaRepository _expedientes = Substitute.For<IExpedienteLojaRepository>();
    private readonly IConsentimentoContatoRepository _consentimentos = Substitute.For<IConsentimentoContatoRepository>();
    private readonly ICanalMensageria _whats = Substitute.For<ICanalMensageria>();
    private readonly IConfiguracaoAtendimentoRepository _configuracoes = Substitute.For<IConfiguracaoAtendimentoRepository>();
    private readonly IStorefrontRepository _storefront = Substitute.For<IStorefrontRepository>();
    private readonly FakeUnitOfWork _uow = new();
    private readonly FakeTimeProvider _relogio = new(new DateTimeOffset(Agora));

    public AutomacaoBancaTests()
    {
        _cliente = new ClienteEntity { Id = Guid.NewGuid(), EmpresaId = _empresaId, Nome = "Ana Souza", Telefone = "11988887777" };
        _clientes.GetByIdAsync(_empresaId, _cliente.Id).Returns(_cliente);
        _consentimentos.ListarDoClienteAsync(_empresaId, _cliente.Id, Arg.Any<CancellationToken>()).Returns(new List<ConsentimentoContato>());
        _whats.Canal.Returns(CanalConversa.WhatsApp);
        _whats.EnviarTextoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("wamid.1");

        _conversa = Conversa.Abrir(_empresaId, "5511988887777", Agora.AddMinutes(-2));
        _conversa.RegistrarEntrada(Agora.AddMinutes(-1));
        _conversa.VincularCliente(_cliente.Id);
        _conversas.ObterPorIdAsync(_empresaId, _conversa.Id, Arg.Any<CancellationToken>()).Returns(_conversa);
        _configuracoes.GetOrDefaultAsync(_empresaId).Returns(ConfiguracaoAtendimento.CriarPadrao(_empresaId));
    }

    private DispararAutomacaoUseCase UseCase() =>
        new(_regras, _conversas, _clientes, _expedientes, new VariaveisAtendimento(_clientes, _pedidos),
            new PoliticaEnvioCliente(_consentimentos), new ResolvedorCanal([_whats]), _uow, _relogio,
            NullLogger<DispararAutomacaoUseCase>.Instance, _pedidos,
            new SaudacaoAtendimento(_storefront, new ConfigurationBuilder().Build()), _configuracoes);

    private void Regra(GatilhoAutomacao gatilho, string texto, bool ligada = true) =>
        _regras.ObterPorGatilhoAsync(_empresaId, gatilho, Arg.Any<CancellationToken>())
            .Returns(RegraAutomatica.Criar(_empresaId, gatilho, texto, ligada, Agora));

    private List<Mensagem> CapturarMensagens()
    {
        var gravadas = new List<Mensagem>();
        _conversas.When(r => r.AddMensagemAsync(Arg.Any<Mensagem>(), Arg.Any<CancellationToken>()))
            .Do(c => gravadas.Add(c.Arg<Mensagem>()));
        return gravadas;
    }

    private PedidoEntity PedidoNoStatus(string status)
    {
        var pedido = PedidoEntity.Criar(_empresaId, _cliente);
        pedido.Status = status;
        _pedidos.GetByIdAsync(_empresaId, pedido.Id).Returns(pedido);
        return pedido;
    }

    // ── atendimento-02 ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FalhaDoCanal_GravaAMensagemComFalhaECodigoEstavel()
    {
        Regra(GatilhoAutomacao.PosEntrega, "Chegou tudo bem?");
        var gravadas = CapturarMensagens();
        _whats.EnviarTextoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<string>>(_ => throw new HttpRequestException(HttpRequestError.ConnectionError, "connect ECONNREFUSED 10.0.0.1"));

        var resultado = await UseCase().ExecuteAsync(new DisparoAutomacao(_empresaId, GatilhoAutomacao.PosEntrega, _conversa.Id, null, null));

        resultado.Should().Be(ResultadoAutomacao.Falhou);
        var falhou = gravadas.Should().ContainSingle("a atendente precisa ver no console que o aviso não saiu").Subject;
        falhou.Status.Should().Be(StatusMensagem.Falhou);
        falhou.Autor.Should().Be(AutorMensagem.Sistema);
        falhou.Texto.Should().Be("Chegou tudo bem?");
        falhou.Erro.Should().Contain("CANAL_SEM_CONEXAO").And.NotContain("ECONNREFUSED");
        falhou.ProximoReenvioEm.Should().BeNull("a automática não é reenviada sozinha: o texto pode estar vencido (#1933)");
        _uow.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task TimeoutDoCanal_EEnvioIncertoEFicaRegistradoComoIncerto()
    {
        Regra(GatilhoAutomacao.PosEntrega, "Chegou?");
        var gravadas = CapturarMensagens();
        _whats.EnviarTextoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<string>>(_ => throw new TaskCanceledException("HttpClient.Timeout"));

        var resultado = await UseCase().ExecuteAsync(new DisparoAutomacao(_empresaId, GatilhoAutomacao.PosEntrega, _conversa.Id, null, null));

        resultado.Should().Be(ResultadoAutomacao.EnvioIncerto, "o timeout não prova que a mensagem não saiu");
        gravadas.Should().ContainSingle().Which.UltimaFalhaEnvio.Should().Be(TipoFalhaEnvio.Incerta);
    }

    [Fact]
    public async Task FalhaComAConversaEncerrada_FicaNoHistoricoQuandoNaoEADespedida()
    {
        // O pagamento confirmado que não chegou deixa rastro mesmo se a conversa foi encerrada antes de o outbox rodar.
        Regra(GatilhoAutomacao.PagamentoConfirmado, "Pago!");
        _conversa.Encerrar(Agora);
        var gravadas = CapturarMensagens();
        _whats.EnviarTextoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<string>>(_ => throw new InvalidOperationException("caiu"));

        (await UseCase().ExecuteAsync(new DisparoAutomacao(_empresaId, GatilhoAutomacao.PagamentoConfirmado, _conversa.Id, _cliente.Id, null)))
            .Should().Be(ResultadoAutomacao.Falhou);

        gravadas.Should().ContainSingle().Which.Status.Should().Be(StatusMensagem.Falhou);
        _conversa.Situacao.Should().Be(SituacaoConversa.Encerrada);
    }

    [Fact]
    public async Task FalhaDaDespedidaComAConversaEncerrada_NaoGravaMensagemFalhaNoHistorico()
    {
        // A despedida do encerramento sai com a conversa já encerrada; a falha dela não reabre nem suja o histórico.
        Regra(GatilhoAutomacao.Encerramento, "Obrigada!");
        _conversa.Encerrar(Agora);
        var gravadas = CapturarMensagens();
        _whats.EnviarTextoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<string>>(_ => throw new InvalidOperationException("caiu"));

        (await UseCase().ExecuteAsync(new DisparoAutomacao(_empresaId, GatilhoAutomacao.Encerramento, _conversa.Id, null, null)))
            .Should().Be(ResultadoAutomacao.Falhou);

        gravadas.Should().BeEmpty();
    }

    private AutomacaoPedidoPagoHandler HandlerDoPago(INotificadorService notificador, PedidoEntity pedido)
    {
        var equipe = new AvisoEquipeNoAparelho(notificador, _conversas, _pedidos, Substitute.For<ITenantContextAccessor>(),
            NullLogger<AvisoEquipeNoAparelho>.Instance);
        _pedidos.GetByIdAsync(_empresaId, pedido.Id).Returns(pedido);
        var aviso = new AvisoStatusPedidoCliente(
            _pedidos, _clientes, Substitute.For<IVagaOcupadaRepository>(), Substitute.For<IStorefrontRepository>(),
            new PoliticaEnvioCliente(_consentimentos), notificador, Substitute.For<ITenantContextAccessor>(),
            NullLogger<AvisoStatusPedidoCliente>.Instance);
        return new AutomacaoPedidoPagoHandler(Substitute.For<ITenantContextAccessor>(), UseCase(), aviso, equipe);
    }

    private OutboxEventoIntegracao EventoPago(PedidoEntity pedido, string provedor = "na_entrega") => OutboxEventoIntegracao.Criar(
        _empresaId, PedidoPagoEvent_Tipo, "Pedido", pedido.Id, JsonSerializer.Serialize(new
        {
            pedidoId = pedido.Id, clienteId = _cliente.Id, conversaId = _conversa.Id,
            cobrancaPedidoId = Guid.NewGuid(), pagamentoExternoId = "pay-1", provedor,
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private const string PedidoPagoEvent_Tipo = EasyStock.Application.Events.Pedidos.PedidoPagoEvent.TipoEvento;

    [Fact]
    public async Task PagamentoConfirmado_EnvioIncertoDaAutomatica_NaoDisparaOAvisoS13()
    {
        // Se a automática chegou ao cliente, o S13 seria o segundo "recebemos seu pagamento".
        var notificador = Substitute.For<INotificadorService>();
        var pedido = PedidoNoStatus(StatusPedidoMapper.Preparando);
        Regra(GatilhoAutomacao.PagamentoConfirmado, "Pago!");
        _whats.EnviarTextoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<string>>(_ => throw new TimeoutException("Meta não respondeu"));

        await HandlerDoPago(notificador, pedido).HandleAsync(EventoPago(pedido), CancellationToken.None);

        await notificador.DidNotReceive().EnfileirarEventoAsync(TipoEventoNotificacao.PedidoPagoConfirmado, Arg.Any<Guid>(),
            Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task PagamentoConfirmado_EnvioIncertoDaAutomatica_AvisaAEquipePeloSino()
    {
        // O S13 não sai (seria o segundo aviso) e o cliente pode estar sem a confirmação: a equipe confere a conversa.
        var notificador = Substitute.For<INotificadorService>();
        var pedido = PedidoNoStatus(StatusPedidoMapper.Preparando);
        Regra(GatilhoAutomacao.PagamentoConfirmado, "Pago!");
        _whats.EnviarTextoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<string>>(_ => throw new TimeoutException("Meta não respondeu"));

        await HandlerDoPago(notificador, pedido).HandleAsync(EventoPago(pedido), CancellationToken.None);

        await notificador.Received(1).EnfileirarEventoAsync(TipoEventoNotificacao.PagamentoSemAvisoAoCliente, _empresaId,
            Arg.Is<string>(p => p.Contains("referencia") && !p.Contains("11988887777")), pedido.Id, Arg.Any<CancellationToken>(),
            Arg.Any<string?>());
    }

    [Fact]
    public async Task PagamentoConfirmado_FalhaDefinitivaDaAutomatica_AindaDisparaOAvisoS13()
    {
        // Controle: recusa da Meta (nada saiu) segue caindo no aviso S13, que tem retentativa.
        var notificador = Substitute.For<INotificadorService>();
        var pedido = PedidoNoStatus(StatusPedidoMapper.Preparando);
        pedido.ClienteTelefone = "11988887777";
        Regra(GatilhoAutomacao.PagamentoConfirmado, "Pago!");
        _whats.EnviarTextoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<string>>(_ => throw new WhatsAppCloudException(131026, "Message undeliverable", ehPermanente: true, 400));

        await HandlerDoPago(notificador, pedido).HandleAsync(EventoPago(pedido), CancellationToken.None);

        await notificador.Received(1).EnfileirarEventoAsync(TipoEventoNotificacao.PedidoPagoConfirmado, _empresaId,
            Arg.Any<string>(), pedido.Id, Arg.Any<CancellationToken>(), Arg.Any<string?>());
    }

    // ── atendimento-06 ───────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(StatusPedidoMapper.SaiuParaEntrega)]
    [InlineData(StatusPedidoMapper.Entregue)]
    public async Task PagamentoConfirmado_PedidoQueJaSaiuOuFoiEntregue_NaoSai(string status)
    {
        // Dinheiro recebido na porta: a quitação publica pedido.pago depois de o pedido sair ou ser entregue.
        var pedido = PedidoNoStatus(status);
        Regra(GatilhoAutomacao.PagamentoConfirmado, "Recebemos seu pagamento!");

        (await UseCase().ExecuteAsync(new DisparoAutomacao(_empresaId, GatilhoAutomacao.PagamentoConfirmado, _conversa.Id, _cliente.Id,
                pedido.Id, PagamentoManual: true)))
            .Should().Be(ResultadoAutomacao.PedidoJaAdiante);

        await _whats.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default);
    }

    [Theory]
    [InlineData(StatusPedidoMapper.SaiuParaEntrega)]
    [InlineData(StatusPedidoMapper.Entregue)]
    public async Task PagamentoConfirmado_PedidoQueJaSaiuOuFoiEntregue_NemOAvisoS13Sai(string status)
    {
        var notificador = Substitute.For<INotificadorService>();
        var pedido = PedidoNoStatus(status);
        pedido.ClienteTelefone = "11988887777";
        // Com a automática desligada o S13 é o único aviso: ele também não pode sair depois da entrega.
        Regra(GatilhoAutomacao.PagamentoConfirmado, "Pago!", ligada: false);

        await HandlerDoPago(notificador, pedido).HandleAsync(EventoPago(pedido), CancellationToken.None);

        await notificador.DidNotReceiveWithAnyArgs().EnfileirarEventoAsync(default, default, default!, default, default);
    }

    [Theory]
    [InlineData(StatusPedidoMapper.SaiuParaEntrega)]
    [InlineData(StatusPedidoMapper.Entregue)]
    public async Task PagamentoConfirmado_PagamentoOnlineComPedidoAdiante_AindaSai(string status)
    {
        // Mercado Pago: o cliente pagou o link e quer a confirmação, mesmo com o pedido já na rua.
        var pedido = PedidoNoStatus(status);
        Regra(GatilhoAutomacao.PagamentoConfirmado, "Recebemos seu pagamento!");

        (await UseCase().ExecuteAsync(new DisparoAutomacao(_empresaId, GatilhoAutomacao.PagamentoConfirmado, _conversa.Id, _cliente.Id,
                pedido.Id, PagamentoManual: false)))
            .Should().Be(ResultadoAutomacao.Enviada);
    }

    [Fact]
    public async Task PagamentoConfirmado_PagamentoOnlineComAutomaticaDesligada_AvisoS13AindaSaiComPedidoEntregue()
    {
        var notificador = Substitute.For<INotificadorService>();
        var pedido = PedidoNoStatus(StatusPedidoMapper.Entregue);
        pedido.ClienteTelefone = "11988887777";
        Regra(GatilhoAutomacao.PagamentoConfirmado, "Pago!", ligada: false);

        await HandlerDoPago(notificador, pedido).HandleAsync(EventoPago(pedido, provedor: "mercadopago"), CancellationToken.None);

        await notificador.Received(1).EnfileirarEventoAsync(TipoEventoNotificacao.PedidoPagoConfirmado, _empresaId,
            Arg.Any<string>(), pedido.Id, Arg.Any<CancellationToken>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task PagamentoConfirmado_PedidoEmPreparo_AindaSai()
    {
        var pedido = PedidoNoStatus(StatusPedidoMapper.Preparando);
        Regra(GatilhoAutomacao.PagamentoConfirmado, "Recebemos seu pagamento!");

        (await UseCase().ExecuteAsync(new DisparoAutomacao(_empresaId, GatilhoAutomacao.PagamentoConfirmado, _conversa.Id, _cliente.Id, pedido.Id)))
            .Should().Be(ResultadoAutomacao.Enviada);
    }

    // ── atendimento-05 ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PosEntrega_VaiPelaConversaDoPedido_NaoPelaMaisRecenteDoCliente()
    {
        var pedido = PedidoNoStatus(StatusPedidoMapper.Entregue);
        pedido.ClienteId = _cliente.Id;
        Regra(GatilhoAutomacao.PosEntrega, "Chegou tudo bem, {nome}?");

        // O cliente também tem uma conversa de outro canal, mais recente, que não é a do pedido.
        var chat = Substitute.For<ICanalMensageria>();
        chat.Canal.Returns(CanalConversa.ChatSite);
        var maisRecente = Conversa.Abrir(_empresaId, "visitante-1", Agora.AddSeconds(-30), "Ana", _cliente.Id, CanalConversa.ChatSite);
        maisRecente.RegistrarEntrada(Agora.AddSeconds(-20));
        _conversas.ListarPorClienteAsync(_empresaId, _cliente.Id, 1, Arg.Any<CancellationToken>()).Returns(new List<Conversa> { maisRecente });
        _conversas.ObterPorIdAsync(_empresaId, maisRecente.Id, Arg.Any<CancellationToken>()).Returns(maisRecente);
        _conversas.ObterIdPorPedidoAsync(_empresaId, pedido.Id, Arg.Any<CancellationToken>()).Returns(_conversa.Id);

        var useCase = new DispararAutomacaoUseCase(_regras, _conversas, _clientes, _expedientes,
            new VariaveisAtendimento(_clientes, _pedidos), new PoliticaEnvioCliente(_consentimentos),
            new ResolvedorCanal([_whats, chat]), _uow, _relogio, NullLogger<DispararAutomacaoUseCase>.Instance, _pedidos);
        var handler = new AutomacaoPedidoEntregueHandler(Substitute.For<ITenantContextAccessor>(), useCase, _pedidos, _conversas);
        var evento = OutboxEventoIntegracao.Criar(_empresaId, AutomacaoPedidoEntregueHandler.Tipo, "Pedido", pedido.Id,
            JsonSerializer.Serialize(new { pedidoId = pedido.Id, statusNovo = StatusPedidoMapper.Entregue },
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        await handler.HandleAsync(evento, CancellationToken.None);

        await _whats.Received(1).EnviarTextoAsync(_conversa.ContatoIdExterno, "Chegou tudo bem, Ana?", Arg.Any<CancellationToken>());
        await chat.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default);
    }

    // ── atendimento-10 ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task VariavelPedido_UsaONumeroDaComandaQuandoAConversaTem()
    {
        // O aviso S13 diz "pedido nº 12"; a automática e o lembrete do link diziam "pedido 3FA9C1B2" do mesmo pedido.
        typeof(Conversa).GetProperty(nameof(Conversa.NumeroComanda))!.SetValue(_conversa, 12L);
        var pedido = PedidoNoStatus(StatusPedidoMapper.Aguardando);
        _conversa.DefinirPedidoEmAndamento(pedido.Id);
        Regra(GatilhoAutomacao.CobrancaVencendo, "O link do pedido {pedido} vence logo.");

        (await UseCase().ExecuteAsync(new DisparoAutomacao(_empresaId, GatilhoAutomacao.CobrancaVencendo, _conversa.Id, _cliente.Id, pedido.Id)))
            .Should().Be(ResultadoAutomacao.Enviada);

        await _whats.Received(1).EnviarTextoAsync(_conversa.ContatoIdExterno, "O link do pedido 12 vence logo.", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VariavelPedido_ComandaDeOutroPedidoDaConversaNaoVale()
    {
        // A comanda é da conversa, não do pedido: a conversa com outro pedido em andamento não empresta o número.
        typeof(Conversa).GetProperty(nameof(Conversa.NumeroComanda))!.SetValue(_conversa, 12L);
        _conversa.DefinirPedidoEmAndamento(Guid.NewGuid());
        var pedido = PedidoNoStatus(StatusPedidoMapper.Aguardando);
        Regra(GatilhoAutomacao.CobrancaVencendo, "O link do pedido {pedido} vence logo.");

        await UseCase().ExecuteAsync(new DisparoAutomacao(_empresaId, GatilhoAutomacao.CobrancaVencendo, _conversa.Id, _cliente.Id, pedido.Id));

        await _whats.Received(1).EnviarTextoAsync(_conversa.ContatoIdExterno,
            $"O link do pedido {pedido.Id.ToString("N")[..8].ToUpperInvariant()} vence logo.", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VariavelPedido_SemComandaSegueOCodigoCurto()
    {
        var pedido = PedidoNoStatus(StatusPedidoMapper.Aguardando);
        Regra(GatilhoAutomacao.CobrancaVencendo, "O link do pedido {pedido} vence logo.");

        await UseCase().ExecuteAsync(new DisparoAutomacao(_empresaId, GatilhoAutomacao.CobrancaVencendo, _conversa.Id, _cliente.Id, pedido.Id));

        await _whats.Received(1).EnviarTextoAsync(_conversa.ContatoIdExterno,
            $"O link do pedido {pedido.Id.ToString("N")[..8].ToUpperInvariant()} vence logo.", Arg.Any<CancellationToken>());
    }

    // ── atendimento-01: quando a automática de primeira entrada não sai, a saudação sai no lugar ─────────────

    private Conversa ConversaNovaSemNome()
    {
        var nova = Conversa.Abrir(_empresaId, "5511977776666", Agora.AddMinutes(-1));
        nova.RegistrarEntrada(Agora.AddMinutes(-1));
        _conversas.ObterPorIdAsync(_empresaId, nova.Id, Arg.Any<CancellationToken>()).Returns(nova);
        return nova;
    }

    [Fact]
    public async Task PrimeiraEntrada_VariavelSemValor_ASaudacaoSaiNoLugar()
    {
        // "Oi {nome}!" para um contato sem nome: a automática não sai e o cliente não pode ficar sem resposta.
        Regra(GatilhoAutomacao.PrimeiroContato, "Oi {nome}!");
        var nova = ConversaNovaSemNome();
        var gravadas = CapturarMensagens();

        (await UseCase().DispararPrimeiraEntradaAsync(_empresaId, nova.Id)).Should().Be(ResultadoAutomacao.VariavelSemValor);

        await _whats.Received(1).EnviarTextoAsync(nova.ContatoIdExterno, Arg.Is<string>(t => t.Contains("/cardapio")), Arg.Any<CancellationToken>());
        gravadas.Should().ContainSingle(m => m.Autor == AutorMensagem.Sistema && m.Status == StatusMensagem.Enviada);
    }

    [Fact]
    public async Task PrimeiraEntrada_FalhaDefinitivaDoCanal_ASaudacaoTentaNoLugar()
    {
        Regra(GatilhoAutomacao.PrimeiroContato, "Oi!");
        var nova = ConversaNovaSemNome();
        _whats.EnviarTextoAsync(nova.ContatoIdExterno, "Oi!", Arg.Any<CancellationToken>())
            .Returns<Task<string>>(_ => throw new WhatsAppCloudException(131026, "Message undeliverable", ehPermanente: true, 400));

        (await UseCase().DispararPrimeiraEntradaAsync(_empresaId, nova.Id)).Should().Be(ResultadoAutomacao.Falhou);

        await _whats.Received(1).EnviarTextoAsync(nova.ContatoIdExterno, Arg.Is<string>(t => t.Contains("/cardapio")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PrimeiraEntrada_ClienteSemConsentimentoTransacional_ASaudacaoNaoSai()
    {
        // O cliente revogou o contato transacional: a saudação também é mensagem para ele.
        Regra(GatilhoAutomacao.PrimeiroContato, "Oi!");
        _consentimentos.ListarDoClienteAsync(_empresaId, _cliente.Id, Arg.Any<CancellationToken>()).Returns([
            ConsentimentoContato.Registrar(_empresaId, _cliente.Id, CanalConversa.WhatsApp, FinalidadeContato.Transacional,
                SituacaoConsentimento.Revogado, "opt_out", Agora)]);

        (await UseCase().DispararPrimeiraEntradaAsync(_empresaId, _conversa.Id)).Should().Be(ResultadoAutomacao.SemConsentimento);

        await _whats.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default);
    }

    [Fact]
    public async Task PrimeiraEntrada_RegraDesligadaQueFicouSemSaudacao_ASaudacaoSai()
    {
        // O webhook cedeu à automática (ligada naquela hora); quando o outbox roda a regra mudou ou a situação virou
        // outra (22h): nada saiu para o cliente.
        var nova = ConversaNovaSemNome();

        (await UseCase().DispararPrimeiraEntradaAsync(_empresaId, nova.Id)).Should().Be(ResultadoAutomacao.SemRegra);

        await _whats.Received(1).EnviarTextoAsync(nova.ContatoIdExterno, Arg.Is<string>(t => t.Contains("/cardapio")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PrimeiraEntrada_RegraDesligadaMasOWebhookJaSaudou_NaoSaiDeNovo()
    {
        var nova = ConversaNovaSemNome();
        nova.RegistrarSaida(Agora.AddSeconds(-20)); // a saudação direta do webhook

        (await UseCase().DispararPrimeiraEntradaAsync(_empresaId, nova.Id)).Should().Be(ResultadoAutomacao.SemRegra);

        await _whats.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default);
    }

    [Fact]
    public async Task PrimeiraEntrada_AutomaticaEnviada_ASaudacaoNaoSai()
    {
        Regra(GatilhoAutomacao.PrimeiroContato, "Oi {nome}!");

        (await UseCase().DispararPrimeiraEntradaAsync(_empresaId, _conversa.Id)).Should().Be(ResultadoAutomacao.Enviada);

        await _whats.Received(1).EnviarTextoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PrimeiraEntrada_EnvioIncerto_ASaudacaoNaoSaiPorCima()
    {
        Regra(GatilhoAutomacao.PrimeiroContato, "Oi!");
        _whats.EnviarTextoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<string>>(_ => throw new TimeoutException("Meta não respondeu"));

        (await UseCase().DispararPrimeiraEntradaAsync(_empresaId, _conversa.Id)).Should().Be(ResultadoAutomacao.EnvioIncerto);

        await _whats.Received(1).EnviarTextoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PrimeiraEntrada_ForaDoHorarioSemAutomatica_OAvisoDoExpedienteSaiNoLugarDaSaudacao()
    {
        _relogio.SetUtcNow(new DateTimeOffset(2026, 10, 1, 3, 0, 0, TimeSpan.Zero)); // 00:00 em São Paulo
        var expediente = ExpedienteLoja.CriarPadrao(_empresaId);
        _expedientes.GetByEmpresaIdAsync(_empresaId, Arg.Any<CancellationToken>()).Returns(expediente);
        var nova = Conversa.Abrir(_empresaId, "5511977776666", _relogio.GetUtcNow().UtcDateTime.AddMinutes(-1));
        nova.RegistrarEntrada(_relogio.GetUtcNow().UtcDateTime.AddMinutes(-1));
        _conversas.ObterPorIdAsync(_empresaId, nova.Id, Arg.Any<CancellationToken>()).Returns(nova);

        await UseCase().DispararPrimeiraEntradaAsync(_empresaId, nova.Id);

        await _whats.Received(1).EnviarTextoAsync(nova.ContatoIdExterno,
            expediente.MensagemParaCliente(_relogio.GetUtcNow().UtcDateTime)!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PrimeiraEntrada_ConversaForaDoWhatsApp_ASaudacaoNaoSai()
    {
        // Só o WhatsApp tem a saudação direta do webhook; chat do site e Instagram não ganham uma nova aqui.
        var chat = Conversa.Abrir(_empresaId, "visitante-1", Agora.AddMinutes(-1), canal: CanalConversa.ChatSite);
        chat.RegistrarEntrada(Agora.AddMinutes(-1));
        _conversas.ObterPorIdAsync(_empresaId, chat.Id, Arg.Any<CancellationToken>()).Returns(chat);

        await UseCase().DispararPrimeiraEntradaAsync(_empresaId, chat.Id);

        await _whats.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default);
    }
}

using System.Text.Json;
using EasyStock.Application.Events.Atendimento;
using EasyStock.Application.Events.Pedidos;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Notifications;
using EasyStock.Application.Tests.Services.Notifications;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Domain.Integration;
using EasyStock.Domain.Sales;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.Events.Atendimento;

/// <summary>
/// #1807: com o console fechado, contato novo, pagamento confirmado e etapa do pedido chegam como Web Push em
/// todos os aparelhos inscritos da empresa. Passa pelo <see cref="NotificadorService"/> real (repositórios falsos):
/// evento de integração → handler → <c>EventoNotificacao</c> → avaliação → outbox Push.
/// </summary>
public class AvisarEquipeNoAparelhoHandlersTests
{
    private static readonly JsonSerializerOptions Camel = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly IPedidoRepository _pedidos = Substitute.For<IPedidoRepository>();
    private readonly IConversaRepository _conversas = Substitute.For<IConversaRepository>();
    private readonly ITenantContextAccessor _tenant = Substitute.For<ITenantContextAccessor>();
    private readonly IEventoNotificacaoRepository _eventoRepository = Substitute.For<IEventoNotificacaoRepository>();
    private readonly IRotinaRepository _rotinaRepository = Substitute.For<IRotinaRepository>();
    private readonly ITemplateRepository _templateRepository = Substitute.For<ITemplateRepository>();
    private readonly IConfiguracaoCanalRepository _configuracaoRepository = Substitute.For<IConfiguracaoCanalRepository>();
    private readonly IBloqueioNotificacaoRepository _bloqueioRepository = Substitute.For<IBloqueioNotificacaoRepository>();
    private readonly IOutboxNotificacaoRepository _outboxRepository = Substitute.For<IOutboxNotificacaoRepository>();
    private readonly NotificadorService _notificador;
    private readonly List<EventoNotificacao> _eventos = [];
    private readonly List<OutboxMensagemNotificacao> _outbox = [];
    private readonly Cliente _cliente;
    private readonly Pedido _pedido;
    private readonly Conversa _conversa;

    public AvisarEquipeNoAparelhoHandlersTests()
    {
        _notificador = new NotificadorService(
            _eventoRepository, _rotinaRepository, _templateRepository, Substitute.For<IConsentimentoRepository>(),
            _configuracaoRepository, _bloqueioRepository, _outboxRepository,
            new NotificadorServiceMetadadosTests.RendererTemplateSimples(), new EasyStock.Application.Services.Notifications.ResolvedorCanal(),
            Substitute.For<IUnitOfWork>(), NullLogger<NotificadorService>.Instance);

        _eventoRepository.When(r => r.AddAsync(Arg.Any<EventoNotificacao>(), Arg.Any<CancellationToken>()))
            .Do(c => _eventos.Add(c.Arg<EventoNotificacao>()));
        _outboxRepository.When(r => r.AddAsync(Arg.Any<OutboxMensagemNotificacao>(), Arg.Any<CancellationToken>()))
            .Do(c => _outbox.Add(c.Arg<OutboxMensagemNotificacao>()));
        _outboxRepository.ExisteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(c => _outbox.Any(o => o.IdempotencyKey == c.Arg<string>()));
        _bloqueioRepository.ListarAtivosAsync(Arg.Any<Guid?>(), Arg.Any<CanalNotificacao?>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _configuracaoRepository.ListarAsync(null, Arg.Any<CancellationToken>())
            .Returns([ConfiguracaoCanal.Criar(CanalNotificacao.Push, "webpush")]);
        _configuracaoRepository.ListarAsync(_empresaId, Arg.Any<CancellationToken>()).Returns([]);

        Configurar(TipoEventoNotificacao.ContatoNovoNoBalcao, "Contato novo", "{{ nome }} entrou em contato");
        Configurar(TipoEventoNotificacao.PedidoPagoNoBalcao, "Pagamento confirmado", "{{ nome }} pagou o pedido");
        Configurar(TipoEventoNotificacao.PedidoMudouEtapaNoBalcao, "Pedido mudou de etapa", "{{ nome }}: {{ etapa }}");

        _cliente = Cliente.Criar(_empresaId, "Maria Souza");
        _cliente.Telefone = "(11) 99999-0001";
        _pedido = Pedido.Criar(_empresaId, _cliente);
        _pedidos.GetByIdAsync(_empresaId, _pedido.Id).Returns(_pedido);
        _conversa = Conversa.Abrir(_empresaId, "5511999990001", DateTime.UtcNow, "Maria Souza");
        _conversas.ObterPorIdAsync(_empresaId, _conversa.Id, Arg.Any<CancellationToken>()).Returns(_conversa);
        _conversas.ObterIdPorPedidoAsync(_empresaId, _pedido.Id, Arg.Any<CancellationToken>()).Returns(_conversa.Id);
    }

    [Fact]
    public async Task ContatoNovoVaiParaTodosOsAparelhosDaEmpresaSoComPrimeiroNome()
    {
        await ContatoNovoAsync(_conversa.Id);

        var push = _outbox.Should().ContainSingle().Subject;
        push.Canal.Should().Be(CanalNotificacao.Push);
        push.Destinatario.Should().Be($"empresa:{_empresaId}");
        push.CorpoRenderizado.Should().Be("Maria entrou em contato");
        push.LerMetadados()!["tag"].Should().Be($"casa-da-baba-contato-novo-{_conversa.Id}",
            "a mesma tag da notificação do console substitui em vez de duplicar");
        _tenant.Received().SetCurrentTenant(_empresaId);
    }

    [Fact]
    public async Task ContatoSemNomeNaoExpoeOTelefone()
    {
        var soTelefone = Conversa.Abrir(_empresaId, "5511988887777", DateTime.UtcNow);
        _conversas.ObterPorIdAsync(_empresaId, soTelefone.Id, Arg.Any<CancellationToken>()).Returns(soTelefone);

        await ContatoNovoAsync(soTelefone.Id);

        _outbox.Should().ContainSingle().Which.CorpoRenderizado.Should().Be("Cliente entrou em contato");
        _eventos.Should().ContainSingle().Which.PayloadJson.Should().NotContain("98888");
    }

    [Fact]
    public async Task ContatoNovoRepetidoNaoDuplica()
    {
        await ContatoNovoAsync(_conversa.Id);
        await ContatoNovoAsync(_conversa.Id);

        _outbox.Should().ContainSingle("o dispatcher de integração entrega at-least-once e a chave é a conversa");
    }

    [Fact]
    public async Task PagamentoConfirmadoAvisaSemValorNemTelefone()
    {
        await PagarAsync("pay-1");
        await PagarAsync("pay-1");

        var push = _outbox.Should().ContainSingle().Subject;
        push.AssuntoRenderizado.Should().Be("Pagamento confirmado");
        push.CorpoRenderizado.Should().Be("Maria pagou o pedido");
        push.LerMetadados()!["tag"].Should().Be($"casa-da-baba-pagamento-{_conversa.Id}");
        _eventos.Should().AllSatisfy(e => e.PayloadJson.Should().NotContain("telefone").And.NotContain("Souza").And.NotContain("total"));
    }

    [Fact]
    public async Task PedidoSemConversaUsaOPedidoNaTag()
    {
        _conversas.ObterIdPorPedidoAsync(_empresaId, _pedido.Id, Arg.Any<CancellationToken>()).Returns((Guid?)null);

        await PagarAsync("pay-1");

        _outbox.Should().ContainSingle().Which.LerMetadados()!["tag"].Should().Be($"casa-da-baba-pagamento-{_pedido.Id}");
    }

    [Theory]
    [InlineData(StatusPedidoMapper.Preparando, "Em preparo")]
    [InlineData(StatusPedidoMapper.Pronto, "Embalado")]
    [InlineData(StatusPedidoMapper.SaiuParaEntrega, "Em entrega")]
    [InlineData(StatusPedidoMapper.Entregue, "Entregue")]
    [InlineData(StatusPedidoMapper.Cancelado, "Cancelado")]
    public async Task EtapaDoPedidoAvisaComORotuloDoConsole(string statusNovo, string rotulo)
    {
        await MudarStatusAsync(StatusPedidoMapper.Aguardando, statusNovo);

        var push = _outbox.Should().ContainSingle().Subject;
        push.CorpoRenderizado.Should().Be($"Maria: {rotulo}");
        push.LerMetadados()!["tag"].Should().Be($"casa-da-baba-etapa-{_conversa.Id}");
    }

    [Theory]
    [InlineData(StatusPedidoMapper.AguardandoPagamento, StatusPedidoMapper.Aguardando)]
    [InlineData(StatusPedidoMapper.Rascunho, StatusPedidoMapper.AguardandoPagamento)]
    [InlineData(StatusPedidoMapper.AguardandoAprovacaoBaba, StatusPedidoMapper.AprovadoBaba)]
    public async Task MudancaQueNaoEEtapaDaEsteiraNaoAvisa(string antigo, string novo)
    {
        // Pago já tem aviso próprio; rascunho e aguardando não são etapa que a equipe precise saber fora do console.
        await MudarStatusAsync(antigo, novo);

        _eventos.Should().BeEmpty();
    }

    [Fact]
    public async Task CadaEtapaAvisaUmaVez()
    {
        await MudarStatusAsync(StatusPedidoMapper.Aguardando, StatusPedidoMapper.Preparando);
        await MudarStatusAsync(StatusPedidoMapper.Aguardando, StatusPedidoMapper.Preparando);
        await MudarStatusAsync(StatusPedidoMapper.Preparando, StatusPedidoMapper.Pronto);

        _outbox.Should().HaveCount(2);
        _outbox.Select(o => o.IdempotencyKey).Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData("Maria Souza", "Maria")]
    [InlineData("  ", "Cliente")]
    [InlineData(null, "Cliente")]
    [InlineData("+55 11 98888-7777", "Cliente")]
    [InlineData("5511988887777", "Cliente")]
    public void PrimeiroNomeNuncaLevaTelefone(string? nome, string esperado) =>
        AvisoEquipeNoAparelho.PrimeiroNome(nome).Should().Be(esperado);

    private async Task ContatoNovoAsync(Guid conversaId)
    {
        var handler = new AvisarEquipeContatoNovoHandler(Aviso());
        await handler.HandleAsync(Evento(ConversaAbertaEvent.TipoEvento, new ConversaAbertaEvent(conversaId, null)), CancellationToken.None);
        await AvaliarEventosAsync();
    }

    private async Task PagarAsync(string pagamentoExternoId)
    {
        var handler = new AvisarEquipePedidoPagoHandler(Aviso());
        var payload = new PedidoPagoEvent(_pedido.Id, _empresaId, null, _cliente.Id, null, Guid.NewGuid(), "mercadopago",
            pagamentoExternoId, "pix", 25m, StatusPedidoMapper.Aguardando, DateTime.UtcNow);
        await handler.HandleAsync(Evento(PedidoPagoEvent.TipoEvento, payload), CancellationToken.None);
        await AvaliarEventosAsync();
    }

    private async Task MudarStatusAsync(string antigo, string novo)
    {
        var handler = new AvisarEquipeEtapaDoPedidoHandler(Aviso());
        var payload = new PedidoMudouStatusEvent(_pedido.Id, _empresaId, null, antigo, novo, "web", null, "Dona", DateTime.UtcNow);
        await handler.HandleAsync(Evento("pedido.mudou_status", payload), CancellationToken.None);
        await AvaliarEventosAsync();
    }

    private async Task AvaliarEventosAsync()
    {
        foreach (var evento in _eventos.Where(e => e.Status == StatusEventoNotificacao.Pendente).ToList())
            await _notificador.AvaliarEventoAsync(evento);
    }

    private AvisoEquipeNoAparelho Aviso() =>
        new(_notificador, _conversas, _pedidos, _tenant, NullLogger<AvisoEquipeNoAparelho>.Instance);

    private OutboxEventoIntegracao Evento<T>(string tipo, T payload) =>
        OutboxEventoIntegracao.Criar(_empresaId, tipo, "pedido", _pedido.Id, JsonSerializer.Serialize(payload, Camel));

    private void Configurar(TipoEventoNotificacao tipo, string assunto, string corpo)
    {
        var codigo = tipo + "_push";
        var rotina = RotinaNotificacao.Criar(codigo + "_rotina", codigo, tipo, TriggerTipoRotina.Evento,
            codigo, CategoriaConteudoNotificacao.Operacional);
        rotina.DefinirFallback("[\"Push\"]", "system");
        _rotinaRepository.ListarAtivasAsync(tipo, Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns([rotina]);
        _templateRepository.GetAtivoAsync(codigo, CanalNotificacao.Push, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var template = TemplateNotificacao.Criar(codigo, codigo, CanalNotificacao.Push, tipo, assunto, corpo);
                template.DefinirMetadados("""{"tag":"{{ tag }}"}""");
                return template;
            });
    }
}

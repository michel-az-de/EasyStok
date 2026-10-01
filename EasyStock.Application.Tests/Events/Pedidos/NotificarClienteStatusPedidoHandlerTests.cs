using System.Text.Json;
using EasyStock.Application.Events.Pedidos;
using EasyStock.Application.Events.Pedidos.Handlers;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Notifications;
using EasyStock.Application.Tests.Services.Notifications;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Domain.Integration;
using EasyStock.Domain.Sales;
using Microsoft.Extensions.Logging.Abstractions;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.Tests.Events.Pedidos;

/// <summary>
/// S13: a transição de status marcada pela dona (<c>pedido.mudou_status</c>) e o pagamento confirmado
/// (<c>pedido.pago</c>) viram aviso ao cliente pelo WhatsApp no outbox de notificações. O teste passa pelo
/// <see cref="NotificadorService"/> real (repositórios falsos): handler → <c>EventoNotificacao</c> → avaliação →
/// <c>OutboxMensagemNotificacao</c>.
/// </summary>
public class NotificarClienteStatusPedidoHandlerTests
{
    private static readonly JsonSerializerOptions Camel = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly IPedidoRepository _pedidos = Substitute.For<IPedidoRepository>();
    private readonly IClienteRepository _clientes = Substitute.For<IClienteRepository>();
    private readonly IVagaOcupadaRepository _vagas = Substitute.For<IVagaOcupadaRepository>();
    private readonly IStorefrontRepository _storefronts = Substitute.For<IStorefrontRepository>();
    private readonly IConsentimentoContatoRepository _consentimentos = Substitute.For<IConsentimentoContatoRepository>();
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

    public NotificarClienteStatusPedidoHandlerTests()
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
            .Returns([ConfiguracaoCanal.Criar(CanalNotificacao.WhatsApp, "meta")]);
        _configuracaoRepository.ListarAsync(_empresaId, Arg.Any<CancellationToken>()).Returns([]);

        Configurar(TipoEventoNotificacao.PedidoPagoConfirmado, "pedido_pago_whatsapp_v1",
            "Recebemos seu pagamento, {{ nome }}! Pedido nº {{ numero }}. Previsão: {{ previsao }}.");
        Configurar(TipoEventoNotificacao.PedidoEmPreparo, "pedido_em_preparo_whatsapp_v1",
            "{{ nome }}, seu pedido nº {{ numero }} está em preparo. Previsão: {{ previsao }}.");
        Configurar(TipoEventoNotificacao.PedidoSaiuParaEntrega, "pedido_saiu_whatsapp_v1",
            "{{ nome }}, seu pedido nº {{ numero }} saiu para entrega.");
        Configurar(TipoEventoNotificacao.PedidoEntregue, "pedido_entregue_whatsapp_v1",
            "Obrigada, {{ nome }}! Siga a gente: {{ instagram }}");
        Configurar(TipoEventoNotificacao.AvaliacaoSolicitada, "avaliacao_whatsapp_v1",
            "{{ nome }}, como foi o pedido nº {{ numero }}?",
            """{"template":"avaliacao","param1":"{{ nome }}","botao1":"acao:avaliacao:positiva:{{ pedidoId }}|Gostei","botao2":"acao:avaliacao:negativa:{{ pedidoId }}|Não gostei"}""");

        _cliente = Cliente.Criar(_empresaId, "Maria Souza");
        _cliente.Telefone = "(11) 99999-0001";
        _pedido = Pedido.Criar(_empresaId, _cliente);
        _pedidos.GetByIdAsync(_empresaId, _pedido.Id).Returns(_pedido);
        _clientes.GetByIdAsync(_empresaId, _cliente.Id).Returns(_cliente);
        _consentimentos.ListarDoClienteAsync(_empresaId, _cliente.Id, Arg.Any<CancellationToken>()).Returns([]);

        var storefront = StorefrontEntity.Criar(_empresaId, "casadababa", "Casa da Babá", 40m);
        storefront.DefinirInstagramUrl("https://instagram.com/casadababa");
        _storefronts.GetByEmpresaAsync(_empresaId, Arg.Any<CancellationToken>()).Returns(storefront);

        var janela = JanelaEntrega.Criar(storefront.Id, 3, new TimeOnly(14, 0), new TimeOnly(18, 0), 10, "Tarde (14h às 18h)");
        var vaga = VagaOcupada.Ocupar(janela.Id, new DateOnly(2026, 10, 1), _pedido.Id);
        _vagas.GetByPedidoIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, (VagaOcupada, JanelaEntrega)> { [_pedido.Id] = (vaga, janela) });
    }

    [Fact]
    public async Task PreparandoEnfileiraComPrevisao()
    {
        await MudarStatusAsync(StatusPedidoMapper.Aguardando, StatusPedidoMapper.Preparando);

        var mensagem = _outbox.Should().ContainSingle().Subject;
        mensagem.Canal.Should().Be(CanalNotificacao.WhatsApp);
        mensagem.Categoria.Should().Be(CategoriaConteudoNotificacao.Transacional);
        mensagem.Destinatario.Should().Be("+5511999990001", "telefone do cliente em E.164");
        mensagem.CorpoRenderizado.Should().Contain("Tarde (14h às 18h), 01/10").And.Contain("Maria");
        _tenant.Received().SetCurrentTenant(_empresaId);
    }

    [Fact]
    public async Task EntregueSempreEnfileira()
    {
        // Cliente revogou marketing no WhatsApp (e a conversa pode estar Assumida): agradecimento é transacional.
        _consentimentos.ListarDoClienteAsync(_empresaId, _cliente.Id, Arg.Any<CancellationToken>()).Returns([
            ConsentimentoContato.Registrar(_empresaId, _cliente.Id, CanalConversa.WhatsApp, FinalidadeContato.Marketing,
                SituacaoConsentimento.Revogado, "opt_out", DateTime.UtcNow)]);

        await MudarStatusAsync(StatusPedidoMapper.SaiuParaEntrega, StatusPedidoMapper.Entregue);

        var mensagem = _outbox.Should().ContainSingle(m => m.CorpoRenderizado.Contains("Obrigada")).Subject;
        mensagem.CorpoRenderizado.Should().Contain("Obrigada, Maria").And.Contain("https://instagram.com/casadababa");
    }

    [Fact]
    public async Task EntregueAgendaAvaliacaoEm30Min()
    {
        // O outbox nasce com o relógio real e AgendarPara não adia para o passado: a entrega fica no
        // futuro, senão o teste quebra quando o relógio passa da data fixa (#1307).
        var agora = DateTime.UtcNow;
        var entregueEm = new DateTime(agora.Year, agora.Month, agora.Day, agora.Hour, 0, 0, DateTimeKind.Utc).AddHours(2);

        await MudarStatusAsync(StatusPedidoMapper.SaiuParaEntrega, StatusPedidoMapper.Entregue, entregueEm);

        var avaliacao = _outbox.Should().ContainSingle(m => m.CorpoRenderizado.Contains("como foi")).Subject;
        avaliacao.ProximaTentativaEm.Should().Be(entregueEm.AddMinutes(30));
        var metadados = avaliacao.LerMetadados()!;
        metadados["template"].Should().Be("avaliacao");
        metadados["botao1"].Should().Be($"acao:avaliacao:positiva:{_pedido.Id}|Gostei");
        metadados["botao2"].Should().Be($"acao:avaliacao:negativa:{_pedido.Id}|Não gostei");
        _outbox.Should().Contain(m => m.CorpoRenderizado.Contains("Obrigada"), "o agradecimento sai na hora");
    }

    [Fact]
    public async Task AvisosDesligadosNaoPedeAvaliacaoMasAgradece()
    {
        _cliente.DefinirAvisosStatus(false, DateTime.UtcNow);

        await MudarStatusAsync(StatusPedidoMapper.SaiuParaEntrega, StatusPedidoMapper.Entregue);

        _outbox.Should().ContainSingle().Which.CorpoRenderizado.Should().Contain("Obrigada");
    }

    [Theory]
    [InlineData(StatusPedidoMapper.Aguardando, StatusPedidoMapper.Preparando)]
    [InlineData(StatusPedidoMapper.Pronto, StatusPedidoMapper.SaiuParaEntrega)]
    public async Task RespeitaAvisosStatus(string statusAnterior, string statusNovo)
    {
        // S24: com AvisosStatusAtivos=false, preparo e saída não enfileiram; entregue enfileira (teste acima).
        _cliente.DefinirAvisosStatus(false, DateTime.UtcNow);

        await MudarStatusAsync(statusAnterior, statusNovo);

        _eventos.Should().BeEmpty();
        _outbox.Should().BeEmpty();
    }

    [Fact]
    public async Task IdempotentePorPedidoEStatus()
    {
        await MudarStatusAsync(StatusPedidoMapper.Pronto, StatusPedidoMapper.SaiuParaEntrega);
        await MudarStatusAsync(StatusPedidoMapper.Pronto, StatusPedidoMapper.SaiuParaEntrega);

        _eventos.Should().HaveCount(2, "o dispatcher de integração entrega at-least-once");
        _outbox.Should().ContainSingle("a chave do outbox é pedido + status novo");
        _outbox[0].IdempotencyKey.Should().Be(OutboxMensagemNotificacao.ComputarIdempotencyKey(
            $"{_pedido.Id:N}|{StatusPedidoMapper.SaiuParaEntrega}", CanalNotificacao.WhatsApp));
    }

    [Theory]
    [InlineData(StatusPedidoMapper.Cancelado)]
    [InlineData(StatusPedidoMapper.Pronto)]
    [InlineData(StatusPedidoMapper.Aguardando)]
    public async Task StatusSemAvisoNaoGeraNada(string statusNovo)
    {
        await MudarStatusAsync(StatusPedidoMapper.Preparando, statusNovo);

        _eventos.Should().BeEmpty();
        _outbox.Should().BeEmpty();
    }

    [Fact]
    public async Task SemTelefoneNaoEnfileira()
    {
        _cliente.Telefone = null;
        _pedido.ClienteTelefone = null;

        await MudarStatusAsync(StatusPedidoMapper.Aguardando, StatusPedidoMapper.Preparando);

        _eventos.Should().BeEmpty();
    }

    [Fact]
    public async Task TransacionalRevogadoNaoEnfileira()
    {
        _consentimentos.ListarDoClienteAsync(_empresaId, _cliente.Id, Arg.Any<CancellationToken>()).Returns([
            ConsentimentoContato.Registrar(_empresaId, _cliente.Id, CanalConversa.WhatsApp, FinalidadeContato.Transacional,
                SituacaoConsentimento.Revogado, "cliente", DateTime.UtcNow)]);

        await MudarStatusAsync(StatusPedidoMapper.Aguardando, StatusPedidoMapper.Preparando);

        _eventos.Should().BeEmpty();
    }

    [Fact]
    public async Task PagamentoConfirmadoAvisaComPrevisao()
    {
        var handler = new NotificarClientePedidoPagoHandler(Aviso());
        var payload = new PedidoPagoEvent(_pedido.Id, _empresaId, null, _cliente.Id, null, Guid.NewGuid(), "mercadopago",
            "pay-1", "pix", 25m, StatusPedidoMapper.Aguardando, DateTime.UtcNow);

        await handler.HandleAsync(Evento(PedidoPagoEvent.TipoEvento, payload), CancellationToken.None);
        await AvaliarEventosAsync();

        var mensagem = _outbox.Should().ContainSingle().Subject;
        mensagem.CorpoRenderizado.Should().Contain("Recebemos seu pagamento").And.Contain("Tarde (14h às 18h), 01/10");
        mensagem.IdempotencyKey.Should().Be(OutboxMensagemNotificacao.ComputarIdempotencyKey(
            $"{_pedido.Id:N}|pago", CanalNotificacao.WhatsApp));
    }

    private async Task MudarStatusAsync(string antigo, string novo, DateTime? ocorridoEm = null)
    {
        var handler = new NotificarClienteStatusPedidoHandler(Aviso());
        var payload = new PedidoMudouStatusEvent(_pedido.Id, _empresaId, null, antigo, novo, "web", null, "Dona", ocorridoEm ?? DateTime.UtcNow);

        await handler.HandleAsync(Evento("pedido.mudou_status", payload), CancellationToken.None);
        await AvaliarEventosAsync();
    }

    private async Task AvaliarEventosAsync()
    {
        foreach (var evento in _eventos.Where(e => e.Status == StatusEventoNotificacao.Pendente).ToList())
            await _notificador.AvaliarEventoAsync(evento);
    }

    private AvisoStatusPedidoCliente Aviso() => new(
        _pedidos, _clientes, _vagas, _storefronts, new PoliticaEnvioCliente(_consentimentos), _notificador, _tenant,
        NullLogger<AvisoStatusPedidoCliente>.Instance);

    private OutboxEventoIntegracao Evento<T>(string tipo, T payload) =>
        OutboxEventoIntegracao.Criar(_empresaId, tipo, "pedido", _pedido.Id, JsonSerializer.Serialize(payload, Camel));

    private void Configurar(TipoEventoNotificacao tipo, string templateCodigo, string corpo, string? metadadosJson = null)
    {
        var rotina = RotinaNotificacao.Criar(templateCodigo + "_rotina", templateCodigo, tipo, TriggerTipoRotina.Evento,
            templateCodigo, CategoriaConteudoNotificacao.Transacional);
        rotina.DefinirFallback("[\"WhatsApp\"]", "system");
        _rotinaRepository.ListarAtivasAsync(tipo, Arg.Any<CancellationToken>()).Returns([rotina]);
        _templateRepository.GetAtivoAsync(templateCodigo, CanalNotificacao.WhatsApp, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var template = TemplateNotificacao.Criar(templateCodigo, templateCodigo, CanalNotificacao.WhatsApp, tipo, "", corpo);
                if (metadadosJson is not null) template.DefinirMetadados(metadadosJson);
                return template;
            });
    }
}

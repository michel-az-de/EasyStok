using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Notifications;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Tests.Services.Notifications;

/// <summary>
/// #2875: o evento <c>ComandaAtualizada</c> que expira antes de ser avaliado nunca vira mensagem do outbox. O texto que o
/// cliente leria sai do mesmo template da rotina e entra na conversa como não entregue, com o motivo.
/// </summary>
public class AvisoEventoExpiradoNaConversaTests
{
    private const string Telefone = "5511987654321";
    private static readonly DateTime Agora = new(2026, 11, 19, 15, 0, 0, DateTimeKind.Utc);
    private readonly Guid _empresa = Guid.NewGuid();
    private readonly Guid _pedido = Guid.NewGuid();
    private readonly IEventoNotificacaoRepository _eventos = Substitute.For<IEventoNotificacaoRepository>();
    private readonly IRotinaRepository _rotinas = Substitute.For<IRotinaRepository>();
    private readonly ITemplateRepository _templates = Substitute.For<ITemplateRepository>();
    private readonly IConversaRepository _conversas = Substitute.For<IConversaRepository>();
    private readonly List<Mensagem> _gravadas = [];
    private readonly AvisoEventoExpiradoNaConversa _servico;

    public AvisoEventoExpiradoNaConversaTests()
    {
        var rotina = RotinaNotificacao.Criar(
            codigo: "comanda_atualizada_global", nome: "Comanda atualizada", tipoEvento: TipoEventoNotificacao.ComandaAtualizada,
            triggerTipo: TriggerTipoRotina.Evento, templateCodigo: "comanda_atualizada_whatsapp_v1",
            categoria: CategoriaConteudoNotificacao.Transacional);
        rotina.DefinirFallback("[\"WhatsApp\"]", "system");
        _rotinas.ListarAtivasAsync(TipoEventoNotificacao.ComandaAtualizada, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns([rotina]);
        _templates.GetAtivoAsync("comanda_atualizada_whatsapp_v1", CanalNotificacao.WhatsApp, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(TemplateNotificacao.Criar(
                codigo: "comanda_atualizada_whatsapp_v1", nome: "Comanda atualizada", canal: CanalNotificacao.WhatsApp,
                tipoEvento: TipoEventoNotificacao.ComandaAtualizada, assuntoTemplate: "",
                corpoTemplate: "{{ nome }}, comanda nº {{ numero }}. {{ mensagem }}"));
        _conversas.When(r => r.AddMensagemAsync(Arg.Any<Mensagem>(), Arg.Any<CancellationToken>()))
            .Do(ci => _gravadas.Add(ci.Arg<Mensagem>()));

        _servico = new AvisoEventoExpiradoNaConversa(_eventos, _rotinas, _templates,
            new NotificadorServiceMetadadosTests.RendererTemplateSimples(), new AvisoNaoEntregueNaConversa(_conversas),
            new PoliticaValidadeNotificacao());
    }

    private EventoNotificacao EventoExpirado(TipoEventoNotificacao tipo = TipoEventoNotificacao.ComandaAtualizada)
    {
        var evento = EventoNotificacao.Criar(tipo, _empresa,
            $$"""{"telefone":"+{{Telefone}}","nome":"Maria","numero":"12","mensagem":"Seu pedido saiu para entrega."}""",
            _pedido);
        evento.MarcarComoExpirado("Expirado: passou do prazo de 120 min sem ser avaliado");
        _eventos.ObterAsync(_empresa, evento.Id, Arg.Any<CancellationToken>()).Returns(evento);
        return evento;
    }

    private Conversa ConversaAberta()
    {
        var conversa = Conversa.Abrir(_empresa, Telefone, Agora.AddDays(-2), "Maria");
        _conversas.ObterAbertaPorContatoAsync(_empresa, CanalConversa.WhatsApp, Telefone, Arg.Any<CancellationToken>())
            .Returns(conversa);
        return conversa;
    }

    [Fact]
    public async Task Evento_expirado_vira_nao_entregue_com_o_texto_do_template_e_o_motivo()
    {
        var conversa = ConversaAberta();
        var evento = EventoExpirado();

        var registrou = await _servico.RegistrarAsync(_empresa, evento.Id, Agora);

        registrou.Should().BeTrue();
        var mensagem = _gravadas.Should().ContainSingle().Subject;
        mensagem.ConversaId.Should().Be(conversa.Id);
        mensagem.Status.Should().Be(StatusMensagem.Falhou);
        mensagem.Texto.Should().Be("Maria, comanda nº 12. Seu pedido saiu para entrega.");
        mensagem.Erro.Should().Be("Não enviada: o aviso ficou 2 h na fila sem sair e expirou.");
    }

    [Fact]
    public async Task Evento_que_nao_expirou_nao_entra_na_conversa()
    {
        ConversaAberta();
        var evento = EventoNotificacao.Criar(TipoEventoNotificacao.ComandaAtualizada, _empresa,
            $$"""{"telefone":"+{{Telefone}}","nome":"Maria","numero":"12"}""", _pedido);
        _eventos.ObterAsync(_empresa, evento.Id, Arg.Any<CancellationToken>()).Returns(evento);

        var registrou = await _servico.RegistrarAsync(_empresa, evento.Id, Agora);

        registrou.Should().BeFalse();
        _gravadas.Should().BeEmpty();
    }

    [Fact]
    public async Task Rotina_sem_WhatsApp_nao_entra_na_conversa()
    {
        ConversaAberta();
        var evento = EventoExpirado();
        var soEmail = RotinaNotificacao.Criar(
            codigo: "comanda_atualizada_global", nome: "Comanda atualizada", tipoEvento: TipoEventoNotificacao.ComandaAtualizada,
            triggerTipo: TriggerTipoRotina.Evento, templateCodigo: "comanda_atualizada_whatsapp_v1",
            categoria: CategoriaConteudoNotificacao.Transacional);
        soEmail.DefinirFallback("[\"Email\"]", "system");
        _rotinas.ListarAtivasAsync(TipoEventoNotificacao.ComandaAtualizada, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns([soEmail]);

        var registrou = await _servico.RegistrarAsync(_empresa, evento.Id, Agora);

        registrou.Should().BeFalse();
        _gravadas.Should().BeEmpty();
    }
}

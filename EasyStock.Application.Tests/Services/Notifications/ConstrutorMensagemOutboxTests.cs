using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Services.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Tests.Services.Notifications;

/// <summary>N5: o único lugar que, dados evento, rotina, canal e destinatário, monta a mensagem do outbox.</summary>
public class ConstrutorMensagemOutboxTests
{
    private const TipoEventoNotificacao Tipo = TipoEventoNotificacao.FaturaVencida;
    private static readonly DateTime Agora = new(DateTime.UtcNow.Year + 1, 10, 2, 15, 0, 0, DateTimeKind.Utc);

    private readonly ITemplateRepository _templates = Substitute.For<ITemplateRepository>();
    private readonly ConstrutorMensagemOutbox _sut;
    private readonly Guid _empresaId = Guid.NewGuid();

    public ConstrutorMensagemOutboxTests()
    {
        _sut = new ConstrutorMensagemOutbox(_templates, new NotificadorServiceMetadadosTests.RendererTemplateSimples());
    }

    private static RotinaNotificacao Rotina(CategoriaConteudoNotificacao categoria = CategoriaConteudoNotificacao.Transacional)
    {
        var r = RotinaNotificacao.Criar("r", "R", Tipo, TriggerTipoRotina.Evento, "fatura_vencida_email_v1", categoria);
        r.DefinirFallback("[\"Email\",\"WhatsApp\"]", "system");
        return r;
    }

    private EventoNotificacao Evento(string payload) => EventoNotificacao.Criar(Tipo, _empresaId, payload);

    private static DestinatarioMensagem Para(EventoNotificacao evento, Guid? usuarioId = null) =>
        new(usuarioId, ConstrutorMensagemOutbox.LerVariaveis(evento.PayloadJson));

    private TemplateNotificacao TemplatePorTipo(
        CanalNotificacao canal, string assunto = "Assunto", string corpo = "Corpo", string? metadados = null)
    {
        var t = TemplateNotificacao.Criar($"x_{canal}", "X", canal, Tipo, assunto, corpo);
        if (metadados is not null) t.DefinirMetadados(metadados);
        _templates.GetAtivoPorTipoAsync(Tipo, canal, Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(t);
        return t;
    }

    [Fact]
    public async Task PayloadComNumeroEBooleanoPreservaTodasAsVariaveis()
    {
        TemplatePorTipo(CanalNotificacao.Email, corpo: "{{ nome }}|{{ qtd }}|{{ ativo }}|{{ valor }}|{{ depois }}");
        var evento = Evento("""{"email":"a@x.com","nome":"Maria","qtd":3,"ativo":true,"valor":1.5,"depois":"ok"}""");

        var r = await _sut.ConstruirAsync(evento, Rotina(), CanalNotificacao.Email, Para(evento), [], Agora);

        r.Mensagem.Should().NotBeNull();
        r.Mensagem!.CorpoRenderizado.Should().Be($"Maria|3|True|{1.5}|ok");
    }

    [Fact]
    public async Task UsaOContatoDoCanalDeDestinoENuncaOAnterior()
    {
        TemplatePorTipo(CanalNotificacao.WhatsApp);
        var evento = Evento("""{"email":"a@x.com","telefone":"+5511999990001"}""");

        var r = await _sut.ConstruirAsync(evento, Rotina(), CanalNotificacao.WhatsApp, Para(evento), [], Agora);

        r.Mensagem!.Destinatario.Should().Be("+5511999990001");
        r.Mensagem.Canal.Should().Be(CanalNotificacao.WhatsApp);
    }

    [Fact]
    public async Task PreservaOsMetadadosDoTemplate()
    {
        TemplatePorTipo(CanalNotificacao.WhatsApp, metadados: """{"template":"fatura","param1":"{{ nome }}"}""");
        var evento = Evento("""{"telefone":"+5511999990001","nome":"Maria"}""");

        var r = await _sut.ConstruirAsync(evento, Rotina(), CanalNotificacao.WhatsApp, Para(evento), [], Agora);

        r.Mensagem!.LerMetadados()!["template"].Should().Be("fatura");
        r.Mensagem.LerMetadados()!["param1"].Should().Be("Maria");
    }

    [Fact]
    public async Task AchaTemplatePorTipoECanalQuandoOCodigoNaoTemOCanal()
    {
        // O código da rotina é o do e-mail: no WhatsApp só a busca por tipo e canal acha (achado 1).
        _templates.GetAtivoAsync("fatura_vencida_email_v1", CanalNotificacao.WhatsApp, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns((TemplateNotificacao?)null);
        var porTipo = TemplatePorTipo(CanalNotificacao.WhatsApp);
        var evento = Evento("""{"telefone":"+5511999990001"}""");

        var r = await _sut.ConstruirAsync(evento, Rotina(), CanalNotificacao.WhatsApp, Para(evento), [], Agora);

        r.Mensagem!.TemplateId.Should().Be(porTipo.Id);
    }

    [Fact]
    public async Task TemplatePorCodigoVenceAPorTipo()
    {
        var porCodigo = TemplateNotificacao.Criar("fatura_vencida_email_v1", "X", CanalNotificacao.Email, Tipo, "A", "B");
        _templates.GetAtivoAsync("fatura_vencida_email_v1", CanalNotificacao.Email, _empresaId, Arg.Any<CancellationToken>())
            .Returns(porCodigo);
        TemplatePorTipo(CanalNotificacao.Email);
        var evento = Evento("""{"email":"a@x.com"}""");

        var r = await _sut.ConstruirAsync(evento, Rotina(), CanalNotificacao.Email, Para(evento), [], Agora);

        r.Mensagem!.TemplateId.Should().Be(porCodigo.Id);
    }

    [Fact]
    public async Task CanalSemTemplateOuSemContatoEPulado()
    {
        var evento = Evento("""{"email":"a@x.com"}""");

        var semTemplate = await _sut.ConstruirAsync(evento, Rotina(), CanalNotificacao.Email, Para(evento), [], Agora);
        semTemplate.Mensagem.Should().BeNull();
        semTemplate.Pulo.Should().Be(MotivoPulo.SemTemplate);

        TemplatePorTipo(CanalNotificacao.WhatsApp);
        var semContato = await _sut.ConstruirAsync(evento, Rotina(), CanalNotificacao.WhatsApp, Para(evento), [], Agora);
        semContato.Mensagem.Should().BeNull();
        semContato.Pulo.Should().Be(MotivoPulo.SemContato);
    }

    [Fact]
    public async Task ChaveDeNegocioEntraNaIdempotencia()
    {
        TemplatePorTipo(CanalNotificacao.Email);
        var evento = Evento("""{"email":"a@x.com","chaveIdempotencia":"fatura:1|vencida"}""");

        var r = await _sut.ConstruirAsync(evento, Rotina(), CanalNotificacao.Email, Para(evento), [], Agora);

        r.Mensagem!.IdempotencyKey.Should().Be(
            OutboxMensagemNotificacao.ComputarIdempotencyKey("fatura:1|vencida", CanalNotificacao.Email));
    }

    [Fact]
    public async Task EventoDeTesteGanhaOPrefixoNoAssunto()
    {
        TemplatePorTipo(CanalNotificacao.Email, assunto: "Fatura");
        var evento = Evento("""{"email":"a@x.com","teste":true}""");

        var r = await _sut.ConstruirAsync(evento, Rotina(), CanalNotificacao.Email, Para(evento), [], Agora);

        r.Mensagem!.AssuntoRenderizado.Should().Be("[TESTE] Fatura");
    }

    [Fact]
    public async Task GravaOsCanaisRestantesParaOFallback()
    {
        TemplatePorTipo(CanalNotificacao.Email);
        var evento = Evento("""{"email":"a@x.com"}""");

        var r = await _sut.ConstruirAsync(
            evento, Rotina(), CanalNotificacao.Email, Para(evento), [CanalNotificacao.WhatsApp], Agora);

        r.Mensagem!.CanaisFallbackRestantesJson.Should().Contain("WhatsApp");
    }

    [Fact]
    public async Task ForaDaJanelaAgendaParaAAberturaMasSegurancaNao()
    {
        TemplatePorTipo(CanalNotificacao.Email);
        var evento = Evento("""{"email":"a@x.com"}""");
        // 15:00 UTC = 12:00 em Brasília; janela 14:00 a 18:00 abre às 17:00 UTC.
        var operacional = Rotina(CategoriaConteudoNotificacao.Operacional);
        operacional.DefinirJanela(new TimeOnly(14, 0), new TimeOnly(18, 0));
        var seguranca = Rotina(CategoriaConteudoNotificacao.Seguranca);
        seguranca.DefinirJanela(new TimeOnly(14, 0), new TimeOnly(18, 0));

        var a = await _sut.ConstruirAsync(evento, operacional, CanalNotificacao.Email, Para(evento), [], Agora);
        var b = await _sut.ConstruirAsync(evento, seguranca, CanalNotificacao.Email, Para(evento), [], Agora);

        a.Mensagem!.ProximaTentativaEm.Should().Be(new DateTime(Agora.Year, 10, 2, 17, 0, 0, DateTimeKind.Utc));
        b.Mensagem!.ProximaTentativaEm.Should().BeBefore(DateTime.UtcNow.AddMinutes(1));
    }
}

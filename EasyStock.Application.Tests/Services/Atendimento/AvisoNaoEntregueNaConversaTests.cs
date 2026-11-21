using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Tests.Services.Atendimento;

/// <summary>
/// #2875: o aviso ao cliente que sai pela fila (<c>ComandaAtualizada</c>, modelo <c>pedido_atualizado</c>) e não chega
/// virava só log do outbox. Agora vira mensagem não entregue na conversa do cliente, com o motivo, e entra em
/// "Não entregues" para a loja reenviar quando a janela abrir.
/// </summary>
public class AvisoNaoEntregueNaConversaTests
{
    private const string Telefone = "5511987654321";
    private const string Corpo = "Maria, comanda nº 12. Seu pedido saiu para entrega.";
    private static readonly DateTime Agora = new(2026, 11, 19, 15, 0, 0, DateTimeKind.Utc);
    private readonly Guid _empresa = Guid.NewGuid();
    private readonly Guid _pedido = Guid.NewGuid();
    private readonly IConversaRepository _conversas = Substitute.For<IConversaRepository>();
    private readonly List<Mensagem> _gravadas = [];
    private readonly AvisoNaoEntregueNaConversa _aviso;

    public AvisoNaoEntregueNaConversaTests()
    {
        _conversas.When(r => r.AddMensagemAsync(Arg.Any<Mensagem>(), Arg.Any<CancellationToken>()))
            .Do(ci => _gravadas.Add(ci.Arg<Mensagem>()));
        _aviso = new AvisoNaoEntregueNaConversa(_conversas);
    }

    private Conversa ConversaAberta()
    {
        var conversa = Conversa.Abrir(_empresa, Telefone, Agora.AddDays(-2), "Maria");
        _conversas.ObterAbertaPorContatoAsync(_empresa, CanalConversa.WhatsApp, Telefone, Arg.Any<CancellationToken>())
            .Returns(conversa);
        return conversa;
    }

    private EventoNotificacao Evento(TipoEventoNotificacao tipo = TipoEventoNotificacao.ComandaAtualizada) =>
        EventoNotificacao.Criar(tipo, _empresa, "{}", _pedido);

    private OutboxMensagemNotificacao Outbox(
        EventoNotificacao evento, CanalNotificacao canal = CanalNotificacao.WhatsApp, string destinatario = Telefone) =>
        OutboxMensagemNotificacao.Criar(evento.Id, Guid.NewGuid(), _empresa, canal, destinatario, "", Corpo,
            CategoriaConteudoNotificacao.Transacional);

    private static ResultadoEnvio Falha(string erro, int? codigo = null, int? statusHttp = null) =>
        new(Sucesso: false, ProviderUsado: "meta", ErroDetalhado: erro, StatusHttp: statusHttp, FalhaPermanente: true)
        {
            Desfecho = DesfechoEnvio.FalhaPermanente,
            CodigoErro = codigo,
        };

    [Fact]
    public async Task Modelo_recusado_pela_Meta_vira_nao_entregue_na_conversa_com_o_motivo()
    {
        var conversa = ConversaAberta();
        var evento = Evento();

        var registrou = await _aviso.RegistrarFalhaAsync(evento, Outbox(evento),
            Falha("(#132001) Template name does not exist in the translation", 132001, 404), Agora);

        registrou.Should().BeTrue();
        var mensagem = _gravadas.Should().ContainSingle().Subject;
        mensagem.ConversaId.Should().Be(conversa.Id);
        mensagem.Status.Should().Be(StatusMensagem.Falhou);
        mensagem.Autor.Should().Be(AutorMensagem.Sistema);
        mensagem.Texto.Should().Be(Corpo);
        mensagem.Erro.Should().StartWith("Não enviada: ").And.Contain("modelo").And.Contain("132001")
            .And.NotContain("Template name");
    }

    [Fact]
    public async Task WhatsApp_da_loja_desconectado_vira_nao_entregue_com_o_motivo()
    {
        ConversaAberta();
        var evento = Evento();

        await _aviso.RegistrarFalhaAsync(evento, Outbox(evento),
            Falha("Conecte o WhatsApp da empresa antes de enviar mensagens.", -2071), Agora);

        var mensagem = _gravadas.Should().ContainSingle().Subject;
        mensagem.Status.Should().Be(StatusMensagem.Falhou);
        mensagem.Erro.Should().Be("Não enviada: Conecte o WhatsApp da empresa antes de enviar mensagens.");
    }

    [Fact]
    public async Task Aviso_expirado_sem_envio_vira_nao_entregue_com_o_motivo()
    {
        ConversaAberta();
        var evento = Evento();

        var registrou = await _aviso.RegistrarExpiradoAsync(evento, Outbox(evento), TimeSpan.FromHours(2), Agora);

        registrou.Should().BeTrue();
        var mensagem = _gravadas.Should().ContainSingle().Subject;
        mensagem.Status.Should().Be(StatusMensagem.Falhou);
        mensagem.Texto.Should().Be(Corpo);
        mensagem.Erro.Should().StartWith("Não enviada: ").And.Contain("2 h").And.Contain("expirou");
    }

    /// <summary>Aceite 4: a mensagem fica em "Não entregues" para a loja reenviar, sem reenvio automático.</summary>
    [Fact]
    public async Task Nao_entregue_pode_ser_reenviada_pela_loja_e_nao_agenda_reenvio_automatico()
    {
        ConversaAberta();
        var evento = Evento();

        await _aviso.RegistrarFalhaAsync(evento, Outbox(evento), Falha("Template paused", 132015, 400), Agora);

        var mensagem = _gravadas.Should().ContainSingle().Subject;
        mensagem.PodeReenviar.Should().BeTrue();
        mensagem.ProximoReenvioEm.Should().BeNull();
    }

    [Fact]
    public async Task Sem_conversa_aberta_do_contato_usa_a_conversa_do_pedido()
    {
        var doPedido = Conversa.Abrir(_empresa, Telefone, Agora.AddDays(-5), "Maria");
        _conversas.ObterIdPorPedidoAsync(_empresa, _pedido, Arg.Any<CancellationToken>()).Returns(doPedido.Id);
        _conversas.ObterPorIdAsync(_empresa, doPedido.Id, Arg.Any<CancellationToken>()).Returns(doPedido);
        var evento = Evento();

        var registrou = await _aviso.RegistrarExpiradoAsync(evento, Outbox(evento), TimeSpan.FromHours(2), Agora);

        registrou.Should().BeTrue();
        _gravadas.Should().ContainSingle().Which.ConversaId.Should().Be(doPedido.Id);
    }

    [Fact]
    public async Task Sem_conversa_nenhuma_nao_grava()
    {
        var evento = Evento();

        var registrou = await _aviso.RegistrarFalhaAsync(evento, Outbox(evento), Falha("Template paused", 132015), Agora);

        registrou.Should().BeFalse();
        _gravadas.Should().BeEmpty();
    }

    [Fact]
    public async Task Telefone_invalido_nao_lanca_e_cai_na_conversa_do_pedido()
    {
        var doPedido = Conversa.Abrir(_empresa, Telefone, Agora.AddDays(-5), "Maria");
        _conversas.ObterIdPorPedidoAsync(_empresa, _pedido, Arg.Any<CancellationToken>()).Returns(doPedido.Id);
        _conversas.ObterPorIdAsync(_empresa, doPedido.Id, Arg.Any<CancellationToken>()).Returns(doPedido);
        var evento = Evento();

        await _aviso.RegistrarExpiradoAsync(evento, Outbox(evento, destinatario: "abc"), TimeSpan.FromHours(2), Agora);

        _gravadas.Should().ContainSingle().Which.ConversaId.Should().Be(doPedido.Id);
    }

    [Fact]
    public async Task Outro_tipo_de_aviso_nao_entra_na_conversa()
    {
        ConversaAberta();
        var evento = Evento(TipoEventoNotificacao.PedidoEntregue);

        var registrou = await _aviso.RegistrarFalhaAsync(evento, Outbox(evento), Falha("Template paused", 132015), Agora);

        registrou.Should().BeFalse();
        _gravadas.Should().BeEmpty();
    }

    [Fact]
    public async Task Aviso_por_outro_canal_nao_entra_na_conversa_do_WhatsApp()
    {
        ConversaAberta();
        var evento = Evento();

        var registrou = await _aviso.RegistrarFalhaAsync(evento,
            Outbox(evento, CanalNotificacao.Email, "maria@exemplo.com"), Falha("caixa cheia"), Agora);

        registrou.Should().BeFalse();
        _gravadas.Should().BeEmpty();
    }

    /// <summary>Cliente que bloqueou ou revogou o transacional pediu para não receber: não vira reenvio na conversa.</summary>
    [Theory]
    [InlineData(RecusasEnvioWhatsApp.ClienteBloqueado)]
    [InlineData(RecusasEnvioWhatsApp.TransacionalRevogado)]
    [InlineData(RecusasEnvioWhatsApp.MarketingRevogado)]
    public async Task Recusa_por_consentimento_nao_entra_na_conversa(string erro)
    {
        ConversaAberta();
        var evento = Evento();

        var registrou = await _aviso.RegistrarFalhaAsync(evento, Outbox(evento), Falha(erro), Agora);

        registrou.Should().BeFalse();
        _gravadas.Should().BeEmpty();
    }
}

/// <summary>#2875: o motivo gravado começa com "Não enviada: " para o Console mostrá-lo como veio (falhaDeEnvio.js).</summary>
public class MotivoAvisoNaoEntregueTests
{
    [Theory]
    [InlineData(132000)]
    [InlineData(132001)]
    [InlineData(132015)]
    public void Codigo_de_modelo_da_Meta_diz_que_o_modelo_foi_recusado(int codigo) =>
        MotivoAvisoNaoEntregue.DaFalha(codigo, 400, "texto da Meta em inglês")
            .Should().Be($"Não enviada: a Meta recusou o modelo da mensagem (código Meta {codigo}).");

    [Fact]
    public void Outro_codigo_da_Meta_guarda_o_codigo_para_o_Console_traduzir() =>
        MotivoAvisoNaoEntregue.DaFalha(131026, 400, "Message undeliverable")
            .Should().Be("Não enviada: o WhatsApp recusou o aviso (código Meta 131026).");

    [Theory]
    [InlineData(-2071, "Conecte o WhatsApp da empresa antes de enviar mensagens.")]
    [InlineData(0, "A empresa não tem phone_number_id do WhatsApp vinculado.")]
    public void Recusa_escrita_por_nos_antes_da_rede_segue_como_veio(int codigo, string erro) =>
        MotivoAvisoNaoEntregue.DaFalha(codigo, null, erro).Should().Be("Não enviada: " + erro);

    [Fact]
    public void Resposta_da_Meta_sem_codigo_nao_expoe_o_texto_cru() =>
        MotivoAvisoNaoEntregue.DaFalha(0, 400, "Unsupported post request")
            .Should().Be("Não enviada: o WhatsApp não aceitou o aviso.");

    [Fact]
    public void Erro_sem_codigo_vira_frase_generica() =>
        MotivoAvisoNaoEntregue.DaFalha(null, null, "System.InvalidOperationException: boom")
            .Should().Be("Não enviada: o WhatsApp não aceitou o aviso.");

    [Fact]
    public void Fora_da_janela_sem_modelo_usa_a_frase_que_o_Console_traduz() =>
        MotivoAvisoNaoEntregue.DaFalha(null, null, RecusasEnvioWhatsApp.ForaDaJanelaSemModelo)
            .Should().Contain("fora da janela de 24 h");

    [Fact]
    public void Expirado_diz_o_prazo_em_horas() =>
        MotivoAvisoNaoEntregue.Expirado(TimeSpan.FromHours(2))
            .Should().Be("Não enviada: o aviso ficou 2 h na fila sem sair e expirou.");

    [Fact]
    public void Expirado_com_prazo_menor_que_uma_hora_diz_em_minutos() =>
        MotivoAvisoNaoEntregue.Expirado(TimeSpan.FromMinutes(30))
            .Should().Be("Não enviada: o aviso ficou 30 min na fila sem sair e expirou.");
}

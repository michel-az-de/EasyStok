using EasyStock.Application.Ports.Output.Atendimento;
using NSubstitute.ExceptionExtensions;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Webhook;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace EasyStock.Application.Tests.Services.Atendimento;

/// <summary>
/// #3117: em 21/11/2026 a ponte ficou CONNECTED por cinco horas sem entregar mensagem e ninguém foi avisado.
/// O vigia mede a entrega (o que a ponte enxerga contra o que o webhook recebeu), não o estado nem o silêncio.
/// </summary>
public class VigiaPonteWppConnectTests
{
    private const string Chat = "5511999999999@c.us";
    private const string Id = "false_5511999999999@c.us_ABC";
    private static readonly DateTimeOffset Agora = new(2026, 11, 22, 3, 0, 0, TimeSpan.Zero);
    private static readonly CredencialWppConnect Sessao =
        new("ez-c8ebd039563e4f34a606c5f7a788c156", "t", Agora.AddDays(-30));

    private readonly IWppConnectSessaoClient _cliente = Substitute.For<IWppConnectSessaoClient>();
    private readonly IWebhookRecebidoRepository _recebidos = Substitute.For<IWebhookRecebidoRepository>();
    private readonly MemoriaVigiaPonteWppConnect _memoria = new();
    private readonly FakeTimeProvider _relogio = new(Agora);
    private readonly ILogger<VigiaPonteWppConnect> _logger = Substitute.For<ILogger<VigiaPonteWppConnect>>();
    private readonly VigiaPonteWppConnect _vigia;

    public VigiaPonteWppConnectTests()
    {
        _vigia = new VigiaPonteWppConnect(_cliente, _recebidos, _memoria, _relogio, _logger);
        // O caso comum: a ponte já estava conectada muito antes das mensagens em jogo.
        _memoria.ObservarConectada(Sessao.Sessao, Agora.AddHours(-12));
        _cliente.ObterEstadoAsync(default!, default).ReturnsForAnyArgs(new EstadoWppConnect(true, true, true, "CONNECTED"));
        _recebidos.ObterAsync(default!, default!, default).ReturnsForAnyArgs((WebhookRecebido?)null);
    }

    private void PonteEnxerga(TimeSpan idade, bool deMim = false, string? tipo = "chat")
    {
        _cliente.ListarUltimasRecebidasAsync(default!, default, default).ReturnsForAnyArgs([new UltimaRecebidaWppConnect(Chat, Id)]);
        _cliente.ListarMensagensRecentesAsync(default!, default!, default).ReturnsForAnyArgs(
            [new MensagemRecenteWppConnect(Id, (Agora - idade).ToUnixTimeSeconds(), deMim, tipo, "texto do cliente")]);
    }

    [Fact]
    public async Task Ponte_conectada_que_enxerga_mensagem_e_nao_entregou_esta_muda()
    {
        PonteEnxerga(TimeSpan.FromMinutes(6));

        (await _vigia.AvaliarAsync(Sessao)).Should().Be(SinalPonteWppConnect.Muda);
        await _cliente.Received(1).ListarUltimasRecebidasAsync(Sessao, VigiaPonteWppConnect.MaximoDeConversas, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Mensagem_entregue_ao_webhook_nao_e_ponte_muda_mesmo_sem_ter_sido_gravada()
    {
        // O registro do webhook mede a entrega: regra de domínio não grava a mensagem, mas ela chegou.
        PonteEnxerga(TimeSpan.FromMinutes(6));
        _recebidos.ObterAsync(ProcessarEventoWhatsAppUseCase.Provedor, Sessao.IdExterno(Id), Arg.Any<CancellationToken>())
            .Returns(WebhookRecebido.Criar(ProcessarEventoWhatsAppUseCase.Provedor, Sessao.IdExterno(Id), "h"));

        (await _vigia.AvaliarAsync(Sessao)).Should().Be(SinalPonteWppConnect.Entregando);
        await _cliente.DidNotReceiveWithAnyArgs().ListarMensagensRecentesAsync(default!, default!, default);
    }

    [Fact]
    public async Task Mensagem_entregue_e_recusada_pela_api_nao_e_ponte_muda()
    {
        // Revisão da #3132: a recusa (400 do parser, 503 do telefone) não grava a idempotência, mas a marca de entrega sim.
        PonteEnxerga(TimeSpan.FromMinutes(6));
        _recebidos.ObterAsync(ProcessarWebhookWppConnectUseCase.ProvedorEntrega, Sessao.IdExterno(Id), Arg.Any<CancellationToken>())
            .Returns(WebhookRecebido.Criar(ProcessarWebhookWppConnectUseCase.ProvedorEntrega, Sessao.IdExterno(Id), "h"));

        (await _vigia.AvaliarAsync(Sessao)).Should().Be(SinalPonteWppConnect.Entregando);
        await _cliente.DidNotReceiveWithAnyArgs().ListarMensagensRecentesAsync(default!, default!, default);
    }

    [Fact]
    public async Task Madrugada_sem_trafego_nao_e_ponte_muda()
    {
        _cliente.ListarUltimasRecebidasAsync(default!, default, default).ReturnsForAnyArgs([]);

        (await _vigia.AvaliarAsync(Sessao)).Should().Be(SinalPonteWppConnect.Entregando);
    }

    [Fact]
    public async Task Mensagem_dentro_da_carencia_ainda_pode_estar_a_caminho()
    {
        PonteEnxerga(TimeSpan.FromMinutes(4));

        (await _vigia.AvaliarAsync(Sessao)).Should().Be(SinalPonteWppConnect.Entregando);
    }

    [Fact]
    public async Task Mensagem_antiga_fora_da_janela_nao_reabre_o_alarme()
    {
        PonteEnxerga(TimeSpan.FromHours(7));

        (await _vigia.AvaliarAsync(Sessao)).Should().Be(SinalPonteWppConnect.Entregando);
    }

    [Fact]
    public async Task Mensagem_anterior_ao_inicio_da_captura_e_descarte_esperado()
    {
        PonteEnxerga(TimeSpan.FromMinutes(30));

        (await _vigia.AvaliarAsync(Sessao with { InicioDaCaptura = Agora.AddMinutes(-10) }))
            .Should().Be(SinalPonteWppConnect.Entregando);
    }

    [Theory]
    [InlineData(true, "chat")]
    [InlineData(false, "e2e_notification")]
    [InlineData(false, null)]
    public async Task Mensagem_da_loja_ou_de_tipo_que_o_webhook_nao_recebe_nao_conta(bool deMim, string? tipo)
    {
        PonteEnxerga(TimeSpan.FromMinutes(6), deMim, tipo);

        (await _vigia.AvaliarAsync(Sessao)).Should().Be(SinalPonteWppConnect.Entregando);
    }

    [Fact]
    public async Task Ponte_desconectada_nao_e_avaliada_nem_consultada()
    {
        _cliente.ObterEstadoAsync(default!, default).ReturnsForAnyArgs(new EstadoWppConnect(true, true, false, "CLOSED"));
        PonteEnxerga(TimeSpan.FromMinutes(6));

        (await _vigia.AvaliarAsync(Sessao)).Should().Be(SinalPonteWppConnect.NaoAvaliada);
        await _cliente.DidNotReceiveWithAnyArgs().ListarUltimasRecebidasAsync(default!, default, default);
    }

    [Fact]
    public async Task Conversa_ja_descartada_nao_gasta_consulta_na_rodada_seguinte()
    {
        // Revisão da #3132: três conversas no topo com última recebida fora da janela (cliente antigo para quem a
        // loja escreveu) gastavam as três consultas em toda rodada e escondiam a conversa realmente muda.
        var antigas = Enumerable.Range(0, 3).Select(i => new UltimaRecebidaWppConnect($"55119999900{i:00}@c.us", $"antiga-{i}")).ToList();
        _cliente.ListarUltimasRecebidasAsync(default!, default, default)
            .ReturnsForAnyArgs([.. antigas, new UltimaRecebidaWppConnect(Chat, Id)]);
        foreach (var antiga in antigas)
            _cliente.ListarMensagensRecentesAsync(Sessao, antiga.Chat, Arg.Any<CancellationToken>()).Returns(
                [new MensagemRecenteWppConnect(antiga.MensagemId, Agora.AddDays(-3).ToUnixTimeSeconds(), false, "chat", "antiga")]);
        _cliente.ListarMensagensRecentesAsync(Sessao, Chat, Arg.Any<CancellationToken>()).Returns(
            [new MensagemRecenteWppConnect(Id, Agora.AddMinutes(-6).ToUnixTimeSeconds(), false, "chat", "texto do cliente")]);

        (await _vigia.AvaliarAsync(Sessao)).Should().Be(SinalPonteWppConnect.Entregando);
        (await _vigia.AvaliarAsync(Sessao)).Should().Be(SinalPonteWppConnect.Muda);
    }

    [Fact]
    public async Task Consulta_no_maximo_tres_conversas_suspeitas_por_rodada()
    {
        var conversas = Enumerable.Range(0, 10).Select(i => new UltimaRecebidaWppConnect($"55119999900{i:00}@c.us", $"id-{i}")).ToList();
        _cliente.ListarUltimasRecebidasAsync(default!, default, default).ReturnsForAnyArgs(conversas);
        _cliente.ListarMensagensRecentesAsync(default!, default!, default).ReturnsForAnyArgs([]);

        (await _vigia.AvaliarAsync(Sessao)).Should().Be(SinalPonteWppConnect.Entregando);
        await _cliente.ReceivedWithAnyArgs(VigiaPonteWppConnect.MaximoDeConsultas).ListarMensagensRecentesAsync(default!, default!, default);
    }

    private VigiaPonteWppConnect VigiaRecemIniciado() =>
        new(_cliente, _recebidos, new MemoriaVigiaPonteWppConnect(), _relogio, _logger);

    private void Ponte(bool conectada) => _cliente.ObterEstadoAsync(default!, default)
        .ReturnsForAnyArgs(new EstadoWppConnect(true, true, conectada, conectada ? "CONNECTED" : "CLOSED"));

    [Fact]
    public async Task Logo_apos_reinicio_do_processo_suspeita_anterior_deixa_a_rodada_sem_conclusao()
    {
        // #3138 (b): este processo ainda não via a ponte quando a mensagem chegou. Pode ter sido sincronizada numa
        // reconexão, sem onmessage: a primeira rodada não abre incidente por ela, nem fecha um que já esteja aberto.
        var vigia = VigiaRecemIniciado();
        PonteEnxerga(TimeSpan.FromMinutes(6));

        (await vigia.AvaliarAsync(Sessao)).Should().Be(SinalPonteWppConnect.NaoAvaliada);
    }

    [Fact]
    public async Task Depois_da_reconexao_mensagem_nova_sem_entrega_continua_sendo_ponte_muda()
    {
        var vigia = VigiaRecemIniciado();
        _cliente.ListarUltimasRecebidasAsync(default!, default, default).ReturnsForAnyArgs([]);
        (await vigia.AvaliarAsync(Sessao)).Should().Be(SinalPonteWppConnect.Entregando);

        _relogio.Advance(TimeSpan.FromMinutes(10));
        _cliente.ListarUltimasRecebidasAsync(default!, default, default).ReturnsForAnyArgs([new UltimaRecebidaWppConnect(Chat, Id)]);
        _cliente.ListarMensagensRecentesAsync(default!, default!, default).ReturnsForAnyArgs(
            [new MensagemRecenteWppConnect(Id, Agora.AddMinutes(4).ToUnixTimeSeconds(), false, "chat", "texto do cliente")]);

        (await vigia.AvaliarAsync(Sessao)).Should().Be(SinalPonteWppConnect.Muda);
    }

    [Fact]
    public async Task Suspeita_anterior_a_conexao_nao_gasta_consulta_de_novo_e_sai_da_conta_ao_cair_da_janela()
    {
        var vigia = VigiaRecemIniciado();
        PonteEnxerga(TimeSpan.FromMinutes(6));

        (await vigia.AvaliarAsync(Sessao)).Should().Be(SinalPonteWppConnect.NaoAvaliada);
        (await vigia.AvaliarAsync(Sessao)).Should().Be(SinalPonteWppConnect.NaoAvaliada);
        await _cliente.ReceivedWithAnyArgs(1).ListarMensagensRecentesAsync(default!, default!, default);

        _relogio.Advance(VigiaPonteWppConnect.Janela);
        (await vigia.AvaliarAsync(Sessao)).Should().Be(SinalPonteWppConnect.Entregando);
    }

    [Fact]
    public async Task Mensagem_muda_vence_a_suspeita_anterior_a_conexao()
    {
        var vigia = VigiaRecemIniciado();
        _cliente.ListarUltimasRecebidasAsync(default!, default, default).ReturnsForAnyArgs([]);
        await vigia.AvaliarAsync(Sessao);
        _relogio.Advance(TimeSpan.FromMinutes(10));

        const string outroChat = "5511988888888@c.us", outroId = "sincronizada";
        _cliente.ListarUltimasRecebidasAsync(default!, default, default)
            .ReturnsForAnyArgs([new UltimaRecebidaWppConnect(outroChat, outroId), new UltimaRecebidaWppConnect(Chat, Id)]);
        _cliente.ListarMensagensRecentesAsync(Sessao, outroChat, Arg.Any<CancellationToken>()).Returns(
            [new MensagemRecenteWppConnect(outroId, Agora.AddMinutes(-30).ToUnixTimeSeconds(), false, "chat", "antes")]);
        _cliente.ListarMensagensRecentesAsync(Sessao, Chat, Arg.Any<CancellationToken>()).Returns(
            [new MensagemRecenteWppConnect(Id, Agora.AddMinutes(4).ToUnixTimeSeconds(), false, "chat", "depois")]);

        (await vigia.AvaliarAsync(Sessao)).Should().Be(SinalPonteWppConnect.Muda);
    }

    private async Task<SinalPonteWppConnect> ObservarAsync(int rodadas, TimeSpan intervalo)
    {
        var sinal = SinalPonteWppConnect.NaoAvaliada;
        for (var i = 0; i < rodadas; i++)
        {
            if (i > 0) _relogio.Advance(intervalo);
            sinal = await _vigia.AvaliarAsync(Sessao);
        }
        return sinal;
    }

    [Fact]
    public async Task Ponte_desconectada_alem_do_prazo_sai_do_escopo_do_vigia()
    {
        // #3138 (c): QR expirado não é ponte muda. Desconectada de vez, o incidente de "muda" não pode ficar aberto.
        Ponte(conectada: false);

        (await ObservarAsync(6, TimeSpan.FromMinutes(5))).Should().Be(SinalPonteWppConnect.NaoAvaliada, "25 minutos");

        _relogio.Advance(TimeSpan.FromMinutes(5));
        (await _vigia.AvaliarAsync(Sessao)).Should().Be(SinalPonteWppConnect.Desconectada);
    }

    [Fact]
    public async Task Tempo_sem_observacoes_seguidas_nao_prova_ponte_desconectada()
    {
        // Banca da #3138: duas leituras a meia hora uma da outra (rodada que falhou, processo parado) não são 30 min seguidos.
        Ponte(conectada: false);

        (await ObservarAsync(2, VigiaPonteWppConnect.PrazoDesconectada)).Should().Be(SinalPonteWppConnect.NaoAvaliada);
    }

    [Fact]
    public async Task Ponte_sem_resposta_conta_como_desconectada()
    {
        // Banca da #3138: contêiner parado lança em vez de responder "desconectada"; o incidente não pode ficar preso por isso.
        _cliente.ObterEstadoAsync(default!, default).ThrowsAsyncForAnyArgs(new HttpRequestException("recusou"));

        (await ObservarAsync(7, TimeSpan.FromMinutes(5))).Should().Be(SinalPonteWppConnect.Desconectada);
    }

    [Fact]
    public async Task Mensagem_sincronizada_em_reconexao_observada_nao_e_ponte_muda()
    {
        // #3138 (b): entrou no aparelho com a ponte desconectada e foi sincronizada sem onmessage. Depois de reiniciar uma
        // ponte muda, é também o que sobra da parada: não pode segurar o incidente aberto.
        Ponte(conectada: false);
        await ObservarAsync(2, TimeSpan.FromMinutes(5));
        _relogio.Advance(TimeSpan.FromMinutes(5));
        Ponte(conectada: true);
        PonteEnxerga(TimeSpan.FromMinutes(6));

        (await _vigia.AvaliarAsync(Sessao)).Should().Be(SinalPonteWppConnect.Entregando);
        (await _vigia.AvaliarAsync(Sessao)).Should().Be(SinalPonteWppConnect.Entregando);
        await _cliente.ReceivedWithAnyArgs(1).ListarMensagensRecentesAsync(default!, default!, default);
    }

    [Fact]
    public async Task Uma_leitura_desconectada_nao_e_queda_e_nao_apaga_a_suspeita()
    {
        // Revisão da #3161: o check-connection oscila. Um timeout no meio de uma parada real não pode descartar a
        // mensagem que prova a ponte muda, senão o incidente fecha com a ponte ainda muda.
        Ponte(conectada: false);
        await _vigia.AvaliarAsync(Sessao);
        _relogio.Advance(TimeSpan.FromMinutes(5));
        Ponte(conectada: true);
        PonteEnxerga(TimeSpan.FromMinutes(11));

        (await _vigia.AvaliarAsync(Sessao)).Should().Be(SinalPonteWppConnect.Muda);
    }

    [Fact]
    public async Task Ponte_sem_resposta_deixa_o_tipo_do_erro_em_log()
    {
        // Revisão da #3161: token errado ou endereço errado no Worker não pode sumir do log e virar "desconectada".
        _cliente.ObterEstadoAsync(default!, default).ThrowsAsyncForAnyArgs(new HttpRequestException("http://ponte:21465 recusou"));

        await _vigia.AvaliarAsync(Sessao);

        var aviso = _logger.ReceivedCalls().Should().ContainSingle().Subject;
        aviso.GetArguments()[0].Should().Be(LogLevel.Warning);
        aviso.GetArguments()[2]!.ToString().Should().Contain("HttpRequestException").And.NotContain("21465");
    }

    [Fact]
    public async Task Mensagem_perdida_com_entrega_posterior_nao_e_ponte_muda()
    {
        // Banca da #3138: a ponte entregou outra mensagem depois desta. É um buraco antigo (a API reiniciando num
        // deploy, uma queda que nenhuma rodada viu), não uma ponte muda.
        PonteEnxerga(TimeSpan.FromMinutes(30));
        _recebidos.UltimoRecebidoEmAsync(ProcessarWebhookWppConnectUseCase.ProvedorEntrega, Sessao.IdExterno(string.Empty),
            Arg.Any<CancellationToken>()).Returns(Agora.AddMinutes(-10).UtcDateTime);

        (await _vigia.AvaliarAsync(Sessao)).Should().Be(SinalPonteWppConnect.Entregando);
    }

    [Fact]
    public async Task Entrega_anterior_a_mensagem_perdida_nao_esconde_a_ponte_muda()
    {
        PonteEnxerga(TimeSpan.FromMinutes(6));
        _recebidos.UltimoRecebidoEmAsync(default!, default!, default).ReturnsForAnyArgs(Agora.AddMinutes(-20).UtcDateTime);

        (await _vigia.AvaliarAsync(Sessao)).Should().Be(SinalPonteWppConnect.Muda);
    }

    [Fact]
    public async Task Reconexao_zera_o_prazo_da_ponte_desconectada()
    {
        _cliente.ListarUltimasRecebidasAsync(default!, default, default).ReturnsForAnyArgs([]);
        Ponte(conectada: false);
        await _vigia.AvaliarAsync(Sessao);
        _relogio.Advance(TimeSpan.FromMinutes(20));
        Ponte(conectada: true);
        await _vigia.AvaliarAsync(Sessao);
        Ponte(conectada: false);
        await _vigia.AvaliarAsync(Sessao);
        _relogio.Advance(TimeSpan.FromMinutes(20));

        (await _vigia.AvaliarAsync(Sessao)).Should().Be(SinalPonteWppConnect.NaoAvaliada);
    }
}

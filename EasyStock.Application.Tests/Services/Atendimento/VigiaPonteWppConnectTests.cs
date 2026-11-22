using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Webhook;
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
    private readonly VigiaPonteWppConnect _vigia;

    public VigiaPonteWppConnectTests()
    {
        _vigia = new VigiaPonteWppConnect(_cliente, _recebidos, new FakeTimeProvider(Agora));
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
    public async Task Consulta_no_maximo_tres_conversas_suspeitas_por_rodada()
    {
        var conversas = Enumerable.Range(0, 10).Select(i => new UltimaRecebidaWppConnect($"55119999900{i:00}@c.us", $"id-{i}")).ToList();
        _cliente.ListarUltimasRecebidasAsync(default!, default, default).ReturnsForAnyArgs(conversas);
        _cliente.ListarMensagensRecentesAsync(default!, default!, default).ReturnsForAnyArgs([]);

        (await _vigia.AvaliarAsync(Sessao)).Should().Be(SinalPonteWppConnect.Entregando);
        await _cliente.ReceivedWithAnyArgs(VigiaPonteWppConnect.MaximoDeConsultas).ListarMensagensRecentesAsync(default!, default!, default);
    }
}

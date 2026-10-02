using System.Text.Json;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Notifications;
using EasyStock.Application.UseCases.Atendimento.Lembretes;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.TestHelpers;
using Microsoft.Extensions.Options;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Lembretes;

/// <summary>N11: o cliente sem resposta vencido também enfileira o <c>PrazoEstourado</c> (e-mail e WhatsApp).</summary>
public class AvaliarLembretesPrazoEstouradoTests
{
    private static readonly DateTime Agora = new(2026, 10, 2, 15, 0, 0, DateTimeKind.Utc);

    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly ILembreteRepository _repo = Substitute.For<ILembreteRepository>();
    private readonly ICandidatosLembreteQuery _candidatos = Substitute.For<ICandidatosLembreteQuery>();
    private readonly INotificadorService _notificador = Substitute.For<INotificadorService>();
    private readonly IOperacaoEventPublisher _publisher = Substitute.For<IOperacaoEventPublisher>();
    private readonly FakeUnitOfWork _uow = new();
    private readonly FakeTimeProvider _relogio = new(new DateTimeOffset(Agora));
    private readonly PrazosOptions _opcoes = new();
    private readonly List<Lembrete> _vencidos = [];

    public AvaliarLembretesPrazoEstouradoTests()
    {
        _candidatos.ListarPedidosSemBaixaAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns([]);
        _candidatos.ListarConversasSemRespostaAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns([]);
        _repo.ListarAutomaticosAbertosAsync(Arg.Any<CancellationToken>()).Returns(_ => []);
        _repo.ListarVencidosSemAvisoAsync(Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => _vencidos.Where(l => l.DeveAvisar(Agora)).ToList());
    }

    private AvaliarLembretesUseCase Avaliador() =>
        new(_repo, _candidatos, _notificador, _publisher, _uow, _relogio, Options.Create(_opcoes));

    private Lembrete Vencido(TipoLembrete tipo, Guid? atendente = null)
    {
        var lembrete = Lembrete.Automatico(_empresaId, tipo, Guid.NewGuid().ToString(), "Fulana está sem resposta.", Agora,
            paraUsuarioId: atendente, conversaId: Guid.NewGuid());
        _vencidos.Add(lembrete);
        return lembrete;
    }

    private static JsonElement Payload(NSubstitute.Core.ICall chamada) =>
        JsonDocument.Parse((string)chamada.GetArguments()[2]!).RootElement;

    private List<NSubstitute.Core.ICall> Prazos() => _notificador.ReceivedCalls()
        .Where(c => c.GetMethodInfo().Name == nameof(INotificadorService.EnfileirarEventoAsync)
                    && (TipoEventoNotificacao)c.GetArguments()[0]! == TipoEventoNotificacao.PrazoEstourado)
        .ToList();

    [Fact]
    public async Task ClienteSemRespostaVencidoEnfileiraPrazoEstourado()
    {
        var lembrete = Vencido(TipoLembrete.ClienteSemResposta);

        for (var rodada = 0; rodada < 3; rodada++) await Avaliador().ExecuteAsync();

        var chamada = Prazos().Should().ContainSingle("rodar três vezes não gera o segundo").Subject;
        var payload = Payload(chamada);
        payload.GetProperty("tipo_legivel").GetString().Should().Be("Cliente sem resposta");
        payload.GetProperty("chaveIdempotencia").GetString().Should().Be($"prazo:cliente_sem_resposta:{lembrete.Id}");
        payload.TryGetProperty("usuarioId", out _).Should().BeFalse("sem atendente, a audiência gestores decide");
        chamada.GetArguments()[5].Should().Be($"prazo:cliente_sem_resposta:{lembrete.Id:N}");
        await _notificador.Received(1).EnfileirarEventoAsync(
            TipoEventoNotificacao.LembreteVencido, _empresaId, Arg.Any<string>(), lembrete.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PagamentoSemBaixaNaoEnfileiraPrazoEstourado()
    {
        Vencido(TipoLembrete.PagamentoSemBaixa);

        await Avaliador().ExecuteAsync();

        Prazos().Should().BeEmpty("pagamento sem baixa fica só no Web Push (fora de Q2)");
    }

    [Fact]
    public async Task LimiteVemDaConfiguracao()
    {
        _opcoes.ClienteSemRespostaMin = 5;

        await Avaliador().ExecuteAsync();

        await _candidatos.Received(1).ListarConversasSemRespostaAsync(Agora - TimeSpan.FromMinutes(5), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SemConfiguracaoOLimiteEDezMinutos()
    {
        await Avaliador().ExecuteAsync();

        await _candidatos.Received(1).ListarConversasSemRespostaAsync(Agora - TimeSpan.FromMinutes(10), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AtendenteAssumidoViraODestinatarioDoAviso()
    {
        var atendente = Guid.NewGuid();
        Vencido(TipoLembrete.ClienteSemResposta, atendente);

        await Avaliador().ExecuteAsync();

        Payload(Prazos().Single()).GetProperty("usuarioId").GetGuid().Should().Be(atendente);
    }

    [Fact]
    public async Task InterruptorDesligadoNaoEnfileiraPrazoMasSegueNoPush()
    {
        _opcoes.Habilitado = false;
        var lembrete = Vencido(TipoLembrete.ClienteSemResposta);

        await Avaliador().ExecuteAsync();

        Prazos().Should().BeEmpty();
        await _notificador.Received(1).EnfileirarEventoAsync(
            TipoEventoNotificacao.LembreteVencido, _empresaId, Arg.Any<string>(), lembrete.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PayloadNaoTemDadoDeCliente()
    {
        Vencido(TipoLembrete.ClienteSemResposta);

        await Avaliador().ExecuteAsync();

        var chaves = Payload(Prazos().Single()).EnumerateObject().Select(p => p.Name).ToList();
        chaves.Should().BeEquivalentTo("tipo_legivel", "referencia", "prazo_texto", "atraso_texto", "chaveIdempotencia");
    }
}

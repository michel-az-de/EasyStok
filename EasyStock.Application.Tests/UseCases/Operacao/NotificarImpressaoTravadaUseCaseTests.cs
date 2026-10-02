using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence.Operacao;
using EasyStock.Application.Services.Notifications;
using EasyStock.Application.UseCases.Operacao.Impressao;
using EasyStock.Domain.Enums.Notifications;
using Microsoft.Extensions.Options;

namespace EasyStock.Application.Tests.UseCases.Operacao;

/// <summary>
/// N11: a impressão travada vira um único <c>PrazoEstourado</c> mesmo com o SSE repetindo a cada rodada, liga o tenant
/// da impressão antes de gravar e lê o limite da configuração.
/// </summary>
public class NotificarImpressaoTravadaUseCaseTests
{
    private static readonly DateTime Agora = new(2026, 10, 2, 15, 0, 0, DateTimeKind.Utc);

    private sealed class RelogioFixo(DateTime utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utc, TimeSpan.Zero);
    }

    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly ITenantContextAccessor _tenant = Substitute.For<ITenantContextAccessor>();
    private readonly IEventoNotificacaoRepository _eventos = Substitute.For<IEventoNotificacaoRepository>();
    private readonly INotificadorService _notificador = Substitute.For<INotificadorService>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IImpressaoPendenteRepository _repo = Substitute.For<IImpressaoPendenteRepository>();
    private readonly IOperacaoEventPublisher _sse = Substitute.For<IOperacaoEventPublisher>();
    private readonly PrazosOptions _opcoes = new();
    private readonly HashSet<string> _gravados = [];

    public NotificarImpressaoTravadaUseCaseTests()
    {
        _eventos.ExisteCorrelacaoAsync(_empresaId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => _gravados.Contains((string)ci[1]));
        _notificador
            .When(n => n.EnfileirarEventoAsync(Arg.Any<TipoEventoNotificacao>(), Arg.Any<Guid>(), Arg.Any<string>(),
                Arg.Any<Guid?>(), Arg.Any<CancellationToken>(), Arg.Any<string?>()))
            .Do(ci => _gravados.Add((string)ci[5]));
    }

    private NotificarImpressaoTravadaUseCase Aviso() =>
        new(_tenant, _eventos, _notificador, _uow, Options.Create(_opcoes), new RelogioFixo(Agora));

    private AlertarImpressoesAtrasadasUseCase Alerta() =>
        new(_repo, _sse, new RelogioFixo(Agora), Options.Create(_opcoes));

    private ImpressaoAtrasada Atrasada() => new(Guid.NewGuid(), _empresaId, Guid.NewGuid(), Agora.AddMinutes(-6));

    [Fact]
    public async Task ImpressaoTravadaGeraUmEventoMesmoComVariasRodadas()
    {
        var impressao = Atrasada();

        var resultados = new List<bool>();
        for (var rodada = 0; rodada < 6; rodada++)
            resultados.Add(await Aviso().ExecuteAsync(impressao));

        resultados.Should().Equal(true, false, false, false, false, false);
        await _notificador.Received(1).EnfileirarEventoAsync(
            TipoEventoNotificacao.PrazoEstourado, _empresaId, Arg.Any<string>(), impressao.ImpressaoId,
            Arg.Any<CancellationToken>(), $"prazo:impressao_travada:{impressao.ImpressaoId:N}");
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task LimiteVemDaConfiguracao()
    {
        _opcoes.ImpressaoPendenteMin = 5;
        _repo.ListarAtrasadasAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ImpressaoAtrasada>());

        await Alerta().ExecuteAsync();

        await _repo.Received(1).ListarAtrasadasAsync(
            Agora.AddHours(-12), Agora.AddMinutes(-5), AlertarImpressoesAtrasadasUseCase.MaximoPorRodada, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SemConfiguracaoOLimiteETresMinutos()
    {
        _repo.ListarAtrasadasAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ImpressaoAtrasada>());

        await Alerta().ExecuteAsync();

        await _repo.Received(1).ListarAtrasadasAsync(
            Agora.AddHours(-12), Agora.AddMinutes(-3), AlertarImpressoesAtrasadasUseCase.MaximoPorRodada, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LigaOTenantDaImpressaoAntesDeGravar()
    {
        var impressao = Atrasada();

        await Aviso().ExecuteAsync(impressao);

        Received.InOrder(() =>
        {
            _tenant.SetCurrentTenant(_empresaId);
            _notificador.EnfileirarEventoAsync(TipoEventoNotificacao.PrazoEstourado, _empresaId, Arg.Any<string>(),
                impressao.ImpressaoId, Arg.Any<CancellationToken>(), Arg.Any<string?>());
            _uow.CommitAsync();
        });
    }

    [Fact]
    public async Task InterruptorDesligadoNaoEnfileira()
    {
        _opcoes.Habilitado = false;

        var avisou = await Aviso().ExecuteAsync(Atrasada());

        avisou.Should().BeFalse();
        _notificador.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task AlertaSegueDevolvendoAsAtrasadasEPublicandoOSse()
    {
        var a = Atrasada();
        _repo.ListarAtrasadasAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([a]);

        var atrasadas = await Alerta().ExecuteAsync();

        atrasadas.Should().ContainSingle().Which.Should().Be(a);
        await _sse.Received(1).PublicarAsync(EventosOperacao.ImpressaoAtrasada, _empresaId,
            Arg.Is<ImpressaoAtrasadaOperacao>(e => e.ImpressaoId == a.ImpressaoId), Arg.Any<CancellationToken>());
    }
}

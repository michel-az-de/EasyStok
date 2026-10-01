using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Lembretes;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.TestHelpers;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Lembretes;

/// <summary>
/// S43: o avaliador cria os lembretes automáticos a partir das consultas, uma vez por fato
/// <c>(Tipo, Referencia)</c>, avisa a dona e resolve sozinho o que deixou de valer.
/// </summary>
public class AvaliadorLembretesTests
{
    private static readonly DateTime Agora = new(2026, 9, 30, 15, 0, 0, DateTimeKind.Utc);

    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly LembreteRepositoryEmMemoria _repo = new();
    private readonly ICandidatosLembreteQuery _candidatos = Substitute.For<ICandidatosLembreteQuery>();
    private readonly INotificadorService _notificador = Substitute.For<INotificadorService>();
    private readonly IOperacaoEventPublisher _publisher = Substitute.For<IOperacaoEventPublisher>();
    private readonly FakeUnitOfWork _uow = new();
    private readonly FakeTimeProvider _relogio = new(new DateTimeOffset(Agora));

    public AvaliadorLembretesTests()
    {
        _candidatos.ListarPedidosSemBaixaAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns([]);
        _candidatos.ListarConversasSemRespostaAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns([]);
    }

    private AvaliarLembretesUseCase Avaliador() => new(_repo, _candidatos, _notificador, _publisher, _uow, _relogio);

    [Fact]
    public async Task PagamentoSemBaixaIdempotente()
    {
        var pedidoId = Guid.NewGuid();
        _candidatos.ListarPedidosSemBaixaAsync(Agora - TimeSpan.FromMinutes(15), Arg.Any<CancellationToken>())
            .Returns([new PedidoSemBaixa(_empresaId, pedidoId, "Fulana", null)]);

        for (var rodada = 0; rodada < 3; rodada++)
        {
            await Avaliador().ExecuteAsync();
            _relogio.Advance(TimeSpan.FromMinutes(1));
            _candidatos.ListarPedidosSemBaixaAsync(_relogio.GetUtcNow().UtcDateTime - TimeSpan.FromMinutes(15), Arg.Any<CancellationToken>())
                .Returns([new PedidoSemBaixa(_empresaId, pedidoId, "Fulana", null)]);
        }

        var lembrete = _repo.Todos.Should().ContainSingle().Subject;
        lembrete.Tipo.Should().Be(TipoLembrete.PagamentoSemBaixa);
        lembrete.PedidoId.Should().Be(pedidoId);
        lembrete.Referencia.Should().Be(pedidoId.ToString());
        lembrete.EstaAberto.Should().BeTrue();
        lembrete.AvisadoEm.Should().Be(Agora, "avisa na rodada em que nasce, e só nela");
        await _notificador.Received(1).EnfileirarEventoAsync(
            TipoEventoNotificacao.LembreteVencido, _empresaId, Arg.Any<string>(), lembrete.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SemRespostaResolveAoResponder()
    {
        var conversaId = Guid.NewGuid();
        var entradaId = Guid.NewGuid();
        var atendente = Guid.NewGuid();
        _candidatos.ListarConversasSemRespostaAsync(Agora - TimeSpan.FromMinutes(10), Arg.Any<CancellationToken>())
            .Returns([new ConversaSemResposta(_empresaId, conversaId, entradaId, "Fulana", atendente)]);

        await Avaliador().ExecuteAsync();

        var lembrete = _repo.Todos.Should().ContainSingle().Subject;
        lembrete.Tipo.Should().Be(TipoLembrete.ClienteSemResposta);
        lembrete.ConversaId.Should().Be(conversaId);
        lembrete.ParaUsuarioId.Should().Be(atendente, "o lembrete é de quem assumiu a conversa (S41)");
        lembrete.EstaAberto.Should().BeTrue();

        // A dona respondeu: a conversa sai da consulta e o lembrete some sozinho.
        _relogio.Advance(TimeSpan.FromMinutes(1));
        _candidatos.ListarConversasSemRespostaAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns([]);

        var resultado = await Avaliador().ExecuteAsync();

        resultado.Resolvidos.Should().Be(1);
        lembrete.EstaAberto.Should().BeFalse();
        lembrete.ConcluidoPorUsuarioId.Should().BeNull("resolveu sozinho, ninguém concluiu");
        _repo.Todos.Should().ContainSingle();
    }

    [Fact]
    public async Task ManualSoAvisaQuandoVence()
    {
        var manual = Lembrete.Manual(_empresaId, "Ligar para o fornecedor", Agora.AddMinutes(30), Guid.NewGuid(), Agora);
        await _repo.AddAsync(manual);

        await Avaliador().ExecuteAsync();
        manual.AvisadoEm.Should().BeNull();

        _relogio.Advance(TimeSpan.FromMinutes(30));
        await Avaliador().ExecuteAsync();

        manual.AvisadoEm.Should().NotBeNull();
        manual.EstaAberto.Should().BeTrue("manual só sai do sininho quando alguém conclui");
        await _publisher.Received(1).PublicarAsync(AvaliarLembretesUseCase.EventoSse, _empresaId, Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LembreteDoVigiaDeIntegracoesNaoEhResolvidoPeloAvaliador()
    {
        // F16 (#1246): o vigia das integrações é quem resolve o lembrete de integração parada.
        await _repo.AddAsync(Lembrete.Automatico(_empresaId, TipoLembrete.IntegracaoParada, "mercadopago:20260930",
            "Integração parada: Mercado Pago.", Agora));

        await Avaliador().ExecuteAsync();

        _repo.Todos.Should().ContainSingle().Which.EstaAberto.Should().BeTrue(
            "o fato (chave quebrada) não está nas consultas do avaliador, e nem por isso deixou de valer");
    }
}

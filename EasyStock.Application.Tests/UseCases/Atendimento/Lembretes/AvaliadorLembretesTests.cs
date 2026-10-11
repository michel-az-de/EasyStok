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
        _candidatos.ListarPedidosAgendadosAsync(Arg.Any<CancellationToken>()).Returns([]);
        _candidatos.ListarPedidosSemBaixaAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns([]);
        _candidatos.ListarConversasSemRespostaAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns([]);
    }

    private AvaliarLembretesUseCase Avaliador() => new(_repo, _candidatos, _notificador, _publisher, _uow, _relogio,
        Microsoft.Extensions.Options.Options.Create(new EasyStock.Application.Services.Notifications.PrazosOptions()));

    private PedidoAgendadoLembrete Agendado(DateTime entrega, DateTime? inicio = null,
        EasyStock.Domain.Entities.Storefront.ExpedienteLoja? expediente = null) =>
        new(_empresaId, Guid.NewGuid(), "Fulana", entrega, inicio, Guid.NewGuid(), expediente);

    private void Agendar(PedidoAgendadoLembrete pedido) =>
        _candidatos.ListarPedidosAgendadosAsync(Arg.Any<CancellationToken>()).Returns([pedido]);

    [Fact]
    public async Task VesperaNoInicioDoExpedienteUmaVezComSse()
    {
        var pedido = Agendado(Agora.Date.AddDays(2).AddHours(21));
        Agendar(pedido);
        (await Avaliador().ExecuteAsync()).Criados.Should().Be(0);
        _relogio.Advance(TimeSpan.FromHours(20).Subtract(TimeSpan.FromSeconds(1))); // 07:59:59 BRT na véspera
        (await Avaliador().ExecuteAsync()).Criados.Should().Be(0);
        _relogio.Advance(TimeSpan.FromSeconds(1));
        (await Avaliador().ExecuteAsync()).Criados.Should().Be(1);
        (await Avaliador().ExecuteAsync()).Criados.Should().Be(0);
        var lembrete = _repo.Todos.Should().ContainSingle().Subject;
        lembrete.Tipo.Should().Be(TipoLembrete.PedidoAgendado);
        lembrete.PedidoId.Should().Be(pedido.PedidoId);
        lembrete.ConversaId.Should().Be(pedido.ConversaId);
        lembrete.Texto.Should().Contain("entrega amanhã").And.Contain("às 18:00");
        lembrete.Referencia!.Length.Should().BeLessThanOrEqualTo(Lembrete.ReferenciaTamanhoMaximo);
        await _notificador.Received(1).EnfileirarEventoAsync(TipoEventoNotificacao.LembreteVencido,
            _empresaId, Arg.Any<string>(), lembrete.Id, Arg.Any<CancellationToken>());
        await _publisher.Received(1).PublicarAsync(AvaliarLembretesUseCase.EventoSse,
            _empresaId, Arg.Is<object>(p => p.GetType().GetProperty("pedidoId")!.GetValue(p)!.Equals(pedido.PedidoId)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InicioPrevistoTemOutroMarcoECancelamentoResolveSemNovoAviso()
    {
        var inicio = Agora.AddMinutes(10);
        Agendar(Agendado(Agora.AddHours(3), inicio));
        (await Avaliador().ExecuteAsync()).Criados.Should().Be(0);
        _relogio.Advance(TimeSpan.FromMinutes(10));
        (await Avaliador().ExecuteAsync()).Avisados.Should().Be(1);
        (await Avaliador().ExecuteAsync()).Avisados.Should().Be(0);
        var lembrete = _repo.Todos.Should().ContainSingle().Subject;
        lembrete.Texto.Should().Contain("início previsto").And.Contain("às 12:10");
        lembrete.Referencia!.Length.Should().BeLessThanOrEqualTo(Lembrete.ReferenciaTamanhoMaximo);
        _candidatos.ListarPedidosAgendadosAsync(Arg.Any<CancellationToken>()).Returns([]);
        var cancelado = await Avaliador().ExecuteAsync();
        cancelado.Resolvidos.Should().Be(1);
        cancelado.Avisados.Should().Be(0);
    }

    // #1550: o marco de início só vale antes do preparo e enquanto é recente. Sem isso, o primeiro tick
    // avisava todo pedido antigo e o pedido já na cozinha recebia "início previsto do preparo".
    [Theory]
    [InlineData(true, 5)]
    [InlineData(false, 24 * 60 + 1)]
    public async Task InicioPrevistoNaoLembraPreparoIniciadoNemInicioAntigo(bool preparoIniciado, int minutosAtras)
    {
        Agendar(Agendado(Agora.AddDays(3), Agora.AddMinutes(-minutosAtras)) with { PreparoIniciado = preparoIniciado });
        var resultado = await Avaliador().ExecuteAsync();
        resultado.Criados.Should().Be(0);
        resultado.Avisados.Should().Be(0);
        _repo.Todos.Should().BeEmpty();
    }

    [Fact]
    public async Task InicioPrevistoValeAteOLimiteEFechaQuandoOPreparoComeca()
    {
        var pedido = Agendado(Agora.AddDays(3), Agora - AvaliarLembretesUseCase.InicioPrevistoValePor);
        Agendar(pedido);
        (await Avaliador().ExecuteAsync()).Criados.Should().Be(1);
        var lembrete = _repo.Todos.Should().ContainSingle().Subject;
        Agendar(pedido with { PreparoIniciado = true });
        var emPreparo = await Avaliador().ExecuteAsync();
        emPreparo.Resolvidos.Should().Be(1);
        emPreparo.Criados.Should().Be(0);
        lembrete.EstaAberto.Should().BeFalse();
        // Voltar à fila não recria: a referência já existe, mesmo concluída.
        Agendar(pedido);
        (await Avaliador().ExecuteAsync()).Criados.Should().Be(0);
        _repo.Todos.Should().ContainSingle();
    }

    // #1550: no dia da entrega o texto "entrega amanhã" já é falso; o aviso fecha sozinho e não renasce.
    [Fact]
    public async Task VesperaFechaSozinhaNoDiaDaEntregaENaoRenasce()
    {
        Agendar(Agendado(Agora.AddDays(1)));
        (await Avaliador().ExecuteAsync()).Criados.Should().Be(1);
        var lembrete = _repo.Todos.Should().ContainSingle().Subject;
        _relogio.Advance(TimeSpan.FromHours(11).Add(TimeSpan.FromMinutes(59))); // 23:59 BRT, ainda véspera
        (await Avaliador().ExecuteAsync()).Resolvidos.Should().Be(0);
        lembrete.EstaAberto.Should().BeTrue();
        _relogio.Advance(TimeSpan.FromMinutes(1)); // 00:00 BRT do dia da entrega
        var noDia = await Avaliador().ExecuteAsync();
        noDia.Resolvidos.Should().Be(1);
        noDia.Criados.Should().Be(0);
        lembrete.EstaAberto.Should().BeFalse();
        _relogio.Advance(TimeSpan.FromDays(1));
        var depois = await Avaliador().ExecuteAsync();
        depois.Criados.Should().Be(0);
        depois.Resolvidos.Should().Be(0);
        _repo.Todos.Should().ContainSingle();
    }

    [Fact]
    public async Task ReagendamentoEncerraVesperaAntigaELembraNovaData()
    {
        var pedido = Agendado(Agora.AddDays(1), Agora.AddDays(1).AddHours(-1));
        Agendar(pedido);
        await Avaliador().ExecuteAsync();
        var anterior = _repo.Todos.Should().ContainSingle().Subject;
        var novo = pedido with { EntregaEm = pedido.EntregaEm.AddDays(2), InicioPrevistoEm = pedido.InicioPrevistoEm!.Value.AddDays(2) };
        Agendar(novo);
        (await Avaliador().ExecuteAsync()).Resolvidos.Should().Be(1);
        _relogio.Advance(TimeSpan.FromDays(2));
        (await Avaliador().ExecuteAsync()).Criados.Should().Be(1);
        anterior.EstaAberto.Should().BeFalse();
        _repo.Todos.Should().ContainSingle(l => l.EstaAberto && l.Referencia != anterior.Referencia);
    }

    [Fact]
    public async Task VesperaUsaTurnoDaEmpresaENaoRetroageNoDiaDaEntrega()
    {
        var expediente = EasyStock.Domain.Entities.Storefront.ExpedienteLoja.CriarPadrao(_empresaId);
        expediente.DefinirHorarios([new((int)Agora.DayOfWeek, new TimeOnly(13, 0), new TimeOnly(22, 0))]);
        Agendar(Agendado(Agora.AddDays(1), expediente: expediente));
        (await Avaliador().ExecuteAsync()).Criados.Should().Be(0); // 12h BRT, abre às 13h
        _relogio.Advance(TimeSpan.FromHours(1));
        (await Avaliador().ExecuteAsync()).Criados.Should().Be(1);
        Agendar(Agendado(Agora.AddDays(1))); // outro pedido já no dia da entrega
        _relogio.Advance(TimeSpan.FromDays(1));
        (await Avaliador().ExecuteAsync()).Criados.Should().Be(0);
    }

    [Fact]
    public async Task SemTurnoNaVesperaOuExpedienteDeOutraEmpresaNaoCriaAviso()
    {
        var expediente = EasyStock.Domain.Entities.Storefront.ExpedienteLoja.CriarPadrao(_empresaId);
        expediente.DefinirHorarios([]);
        Agendar(Agendado(Agora.AddDays(1), expediente: expediente));
        (await Avaliador().ExecuteAsync()).Criados.Should().Be(0);
        Agendar(Agendado(Agora.AddDays(1), Agora, EasyStock.Domain.Entities.Storefront.ExpedienteLoja.CriarPadrao(Guid.NewGuid())));
        (await Avaliador().ExecuteAsync()).Criados.Should().Be(0);
    }

    [Fact]
    public async Task FecharNaMaoNaoConcluiLembreteAindaSemRespostaNemDisparaAviso()
    {
        var conversaId = Guid.NewGuid();
        var entradaId = Guid.NewGuid();
        var expediente = EasyStock.Domain.Entities.Storefront.ExpedienteLoja.CriarPadrao(_empresaId);
        var lembrete = Lembrete.Automatico(_empresaId, TipoLembrete.ClienteSemResposta, entradaId.ToString(),
            "Sem resposta", Agora, conversaId: conversaId);
        await _repo.AddAsync(lembrete);
        expediente.DefinirControle(EasyStock.Domain.Enums.Storefront.ControleManualLoja.ForcarFechada, null, Agora);
        _candidatos.ListarConversasSemRespostaAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([new ConversaSemResposta(_empresaId, conversaId, entradaId, "Fulana", null, Agora.AddMinutes(-20), 5, expediente)]);
        var resultado = await Avaliador().ExecuteAsync();
        resultado.Resolvidos.Should().Be(0);
        resultado.Avisados.Should().Be(0);
        lembrete.EstaAberto.Should().BeTrue();
        expediente.DefinirControle(EasyStock.Domain.Enums.Storefront.ControleManualLoja.ForcarAberta, null, Agora);
        (await Avaliador().ExecuteAsync()).Avisados.Should().Be(1);
        _repo.Todos.Should().ContainSingle();
    }

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
        _candidatos.ListarConversasSemRespostaAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([new ConversaSemResposta(_empresaId, conversaId, entradaId, "Fulana", atendente, Agora.AddMinutes(-11))]);

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

    /// <summary>Repositório em memória: a idempotência é do avaliador, não do mock.</summary>
    private sealed class LembreteRepositoryEmMemoria : ILembreteRepository
    {
        private readonly List<Lembrete> _itens = [];
        public IReadOnlyList<Lembrete> Todos => _itens;

        public Task AddAsync(Lembrete lembrete, CancellationToken ct = default)
        {
            _itens.Add(lembrete);
            return Task.CompletedTask;
        }

        public Task<Lembrete?> ObterAsync(Guid empresaId, Guid id, CancellationToken ct = default) =>
            Task.FromResult(_itens.FirstOrDefault(l => l.EmpresaId == empresaId && l.Id == id));

        public Task<IReadOnlyList<Lembrete>> ListarAsync(
            Guid empresaId, Guid? usuarioId, bool incluirConcluidos, int limite, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Lembrete>>(_itens
                .Where(l => l.EmpresaId == empresaId && (incluirConcluidos || l.EstaAberto)
                    && (usuarioId is null || l.ParaUsuarioId is null || l.ParaUsuarioId == usuarioId))
                .Take(limite).ToList());

        public Task<int> MarcarVencidosVistosAsync(Guid empresaId, Guid usuarioId, DateTime agoraUtc, CancellationToken ct = default)
        {
            var vencidos = _itens.Where(l => l.EmpresaId == empresaId && l.EstaAberto
                && (l.ParaUsuarioId is null || l.ParaUsuarioId == usuarioId)
                && l.VistoEm is null && l.VenceEm <= agoraUtc).ToList();
            foreach (var lembrete in vencidos) lembrete.MarcarVisto(agoraUtc);
            return Task.FromResult(vencidos.Count);
        }

        public Task<bool> ExisteAutomaticoAsync(Guid empresaId, TipoLembrete tipo, string referencia, CancellationToken ct = default) =>
            Task.FromResult(_itens.Any(l => l.EmpresaId == empresaId && l.Tipo == tipo && l.Referencia == referencia));

        public Task<IReadOnlyList<Lembrete>> ListarAutomaticosAbertosAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Lembrete>>(_itens.Where(l => l.Tipo != TipoLembrete.Manual && l.EstaAberto).ToList());

        public Task<IReadOnlyList<Lembrete>> ListarVencidosSemAvisoAsync(DateTime agoraUtc, int limite, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Lembrete>>(_itens.Where(l => l.DeveAvisar(agoraUtc)).Take(limite).ToList());
    }
}

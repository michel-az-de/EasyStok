using EasyStock.Application.Ports.Output.Persistence;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Notifications;
using EasyStock.Application.UseCases.Atendimento.Avisos;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Avisos;

/// <summary>#2096: a vigia avisa uma vez cada coisa que nenhum fato do sistema avisa sozinho.</summary>
public class VigiarOperacaoUseCaseTests
{
    // 15:00 UTC = 12:00 de Brasília: depois da hora do resumo do freezer.
    private static readonly DateTime Agora = new(2026, 10, 27, 15, 0, 0, DateTimeKind.Utc);

    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly IConfiguracaoAtendimentoRepository _configuracoes = Substitute.For<IConfiguracaoAtendimentoRepository>();
    private readonly IVigiaOperacaoQuery _consultas = Substitute.For<IVigiaOperacaoQuery>();
    private readonly IEventoNotificacaoRepository _eventos = Substitute.For<IEventoNotificacaoRepository>();
    private readonly INotificadorService _notificador = Substitute.For<INotificadorService>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly PrazosOptions _prazos = new();

    public VigiarOperacaoUseCaseTests()
    {
        _consultas.ListarProntosSemEntregadorAsync(default, default, default, default).ReturnsForAnyArgs([]);
        _consultas.ListarEncomendasAsync(default, default, default, default).ReturnsForAnyArgs([]);
        _consultas.ListarFreezerParaConferirAsync(default, default, default, default).ReturnsForAnyArgs([]);
        Avisos(AvisosOperacao.Padrao);
    }

    private void Avisos(AvisosOperacao avisos)
    {
        var c = ConfiguracaoAtendimento.CriarPadrao(_empresaId);
        c.DefinirAvisos(avisos);
        _configuracoes.GetOrDefaultAsync(_empresaId).Returns(c);
    }

    private VigiarOperacaoUseCase Criar() => new(_configuracoes, _consultas, _eventos, _notificador, _uow,
        Options.Create(_prazos), NullLogger<VigiarOperacaoUseCase>.Instance);

    private List<(TipoEventoNotificacao Tipo, JsonElement Payload, string Correlacao)> Enfileirados() =>
        _notificador.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(INotificadorService.EnfileirarEventoAsync))
            .Select(c => c.GetArguments())
            .Select(a => ((TipoEventoNotificacao)a[0]!, JsonDocument.Parse((string)a[2]!).RootElement, (string)a[5]!))
            .ToList();

    [Fact]
    public async Task ProntoParadoSemEntregadorViraPrazoEstouradoComOTempo()
    {
        var pedido = Guid.NewGuid();
        _consultas.ListarProntosSemEntregadorAsync(_empresaId, Agora.AddHours(-24), Agora.AddMinutes(-15), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([new PedidoProntoSemEntregador(pedido, "Maria Souza", Agora.AddMinutes(-20))]);

        var r = await Criar().ExecuteAsync(_empresaId, Agora);

        r.ProntosSemEntregador.Should().Be(1);
        var (tipo, payload, correlacao) = Enfileirados().Single();
        tipo.Should().Be(TipoEventoNotificacao.PrazoEstourado);
        correlacao.Should().Be($"prazo:pronto_sem_entregador:{pedido:N}");
        payload.GetProperty("aviso").GetString().Should().Be(CodigosAviso.ProntoSemEntregador);
        payload.GetProperty("atraso_texto").GetString().Should().Be("20 minutos");
        payload.GetProperty("cliente").GetString().Should().Be("Maria");
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task ProntoJaAvisadoNaoRepete()
    {
        var pedido = Guid.NewGuid();
        _consultas.ListarProntosSemEntregadorAsync(default, default, default, default).ReturnsForAnyArgs(
            [new PedidoProntoSemEntregador(pedido, "Maria", Agora.AddMinutes(-40))]);
        _eventos.ExisteCorrelacaoAsync(_empresaId, $"prazo:pronto_sem_entregador:{pedido:N}", Arg.Any<CancellationToken>()).Returns(true);

        var r = await Criar().ExecuteAsync(_empresaId, Agora);

        r.Total.Should().Be(0);
        Enfileirados().Should().BeEmpty();
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task TempoZeroNemConsulta()
    {
        Avisos(new AvisosOperacao
        {
            ProntoSemEntregadorMinutos = 0, EncomendaProximaHoras = 0, EncomendaSemConfirmacaoHoras = 0,
            EstoqueVencendoDias = 0, EstoqueParadoDias = 0,
        });

        await Criar().ExecuteAsync(_empresaId, Agora);

        _consultas.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task InterruptorGeralDesligadoNaoFazNada()
    {
        _prazos.Habilitado = false;

        await Criar().ExecuteAsync(_empresaId, Agora);

        _consultas.ReceivedCalls().Should().BeEmpty();
        _configuracoes.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task EncomendaSemConfirmacaoAvisaAsDuasCoisasQuandoEstaPerto()
    {
        var pedido = Guid.NewGuid();
        var agendado = Agora.AddHours(1);
        _consultas.ListarEncomendasAsync(_empresaId, Agora, Agora.AddHours(24), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([new EncomendaEmAberto(pedido, "João", agendado, null, Agora.AddDays(-2))]);

        var r = await Criar().ExecuteAsync(_empresaId, Agora);

        r.EncomendasProximas.Should().Be(1);
        r.EncomendasSemConfirmacao.Should().Be(1);
        var avisos = Enfileirados();
        avisos.Should().OnlyContain(a => a.Tipo == TipoEventoNotificacao.AlertaOperacao);
        avisos.Select(a => a.Correlacao).Should().BeEquivalentTo(
            $"av:ep:{pedido:N}:2610271600", $"av:ec:{pedido:N}:2610271600");
        avisos.Should().OnlyContain(a => a.Correlacao.Length <= 64);
        avisos[0].Payload.GetProperty("resumo_curto").GetString().Should().Be("João: encomenda 27/10 às 13:00");
    }

    [Fact]
    public async Task EncomendaConfirmadaLongeSoEntraNoSemConfirmacaoSeFaltarConfirmar()
    {
        _consultas.ListarEncomendasAsync(default, default, default, default).ReturnsForAnyArgs(
        [
            new EncomendaEmAberto(Guid.NewGuid(), "Ana", Agora.AddHours(10), null, Agora.AddDays(-3)),
            new EncomendaEmAberto(Guid.NewGuid(), "Bia", Agora.AddHours(10), Agora.AddDays(-1), Agora.AddDays(-3)),
        ]);

        var r = await Criar().ExecuteAsync(_empresaId, Agora);

        r.EncomendasProximas.Should().Be(0, "10 h passa das 2 h do aviso de encomenda chegando");
        r.EncomendasSemConfirmacao.Should().Be(1, "só a Ana falta confirmar");
    }

    [Fact]
    public async Task PedidoParaHojeNaoEhEncomenda()
    {
        // O site grava a data em todo pedido: o pedido de hoje para hoje já está na cozinha, não é encomenda.
        _consultas.ListarEncomendasAsync(default, default, default, default).ReturnsForAnyArgs(
            [new EncomendaEmAberto(Guid.NewGuid(), "Caio", Agora.AddHours(1), null, Agora.AddMinutes(-10))]);

        var r = await Criar().ExecuteAsync(_empresaId, Agora);

        r.Total.Should().Be(0);
        Enfileirados().Should().BeEmpty();
    }

    [Theory]
    [InlineData(2026, 10, 27, 2, 59, false)] // 23:59 de 26/10 em Brasília para 27/10: véspera
    [InlineData(2026, 10, 27, 3, 0, true)]   // 00:00 de 27/10 em Brasília: mesmo dia
    public void EncomendaUsaODiaDeBrasilia(int ano, int mes, int dia, int hora, int minuto, bool mesmoDia)
    {
        var criado = new DateTime(ano, mes, dia, hora, minuto, 0, DateTimeKind.Utc);
        var e = new EncomendaEmAberto(Guid.NewGuid(), null, new DateTime(2026, 10, 27, 15, 0, 0, DateTimeKind.Utc), null, criado);

        VigiarOperacaoUseCase.EhEncomenda(e).Should().Be(!mesmoDia);
    }

    [Fact]
    public async Task FalhaNoCommitNaoPerdeOFreezerDoDia()
    {
        _consultas.ListarFreezerParaConferirAsync(default, default, default, default).ReturnsForAnyArgs(
            [new ItemFreezerParaConferir("Lasanha", "L-1", Agora.AddDays(-20), null, 1)]);
        _uow.CommitAsync().Returns(_ => throw new InvalidOperationException("banco fora"), _ => Task.FromResult(1));
        var uc = Criar();

        await uc.Invoking(u => u.ExecuteAsync(_empresaId, Agora)).Should().ThrowAsync<InvalidOperationException>();
        var segunda = await uc.ExecuteAsync(_empresaId, Agora.AddMinutes(1));

        segunda.ResumosEstoque.Should().Be(1);
    }

    [Fact]
    public async Task FreezerSaiUmaVezPorDiaDepoisDas8()
    {
        _consultas.ListarFreezerParaConferirAsync(_empresaId, Agora.AddDays(2), Agora.AddDays(-10), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(
            [
                new ItemFreezerParaConferir("Lasanha", "L-101", Agora.AddDays(-5), Agora.AddDays(1), 3),
                new ItemFreezerParaConferir("Nhoque", "L-090", Agora.AddDays(-30), Agora.AddDays(60), 1),
            ]);
        var uc = Criar();

        var primeira = await uc.ExecuteAsync(_empresaId, Agora);
        var segunda = await uc.ExecuteAsync(_empresaId, Agora.AddMinutes(1));

        primeira.ResumosEstoque.Should().Be(1);
        segunda.ResumosEstoque.Should().Be(0);
        var (_, payload, correlacao) = Enfileirados().Single();
        correlacao.Should().Be($"av:es:{_empresaId:N}:20261027");
        payload.GetProperty("resumo_curto").GetString().Should().Be("1 item perto de vencer e 1 item para conferir no freezer");
        payload.GetProperty("texto").GetString().Should()
            .Contain("Lasanha (lote L-101): vence em 28/10, 3 unidades")
            .And.Contain("Nhoque (lote L-090): produzido em 27/09 e sem baixa, 1 unidade");
    }

    [Fact]
    public async Task FreezerAntesDas8NaoConsulta()
    {
        await Criar().ExecuteAsync(_empresaId, new DateTime(2026, 10, 27, 10, 0, 0, DateTimeKind.Utc)); // 07:00 de Brasília

        await _consultas.DidNotReceiveWithAnyArgs().ListarFreezerParaConferirAsync(default, default, default, default);
    }

    [Fact]
    public void TextoDoFreezerCortaNoLimiteEDizQuantosFaltam()
    {
        var itens = Enumerable.Range(1, 11)
            .Select(i => new ItemFreezerParaConferir($"Item {i}", $"L{i}", Agora.AddDays(-20), null, 1)).ToList();

        var texto = VigiarOperacaoUseCase.TextoFreezer(itens, Agora.AddDays(2), Agora);

        texto.Should().Contain("Item 8 (").And.NotContain("Item 9 (").And.EndWith("E mais 3 na tela de produção.");
    }
}

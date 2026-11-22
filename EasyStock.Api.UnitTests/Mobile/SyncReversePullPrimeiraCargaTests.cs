using EasyStock.Api.Mobile.DTOs;
using EasyStock.Api.Mobile.Services;
using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Mobile;

/// <summary>
/// #2813 (B5): o aparelho novo recebia o histórico inteiro do ERP (pedido cancelado, lote e fechamento
/// antigos) e a tela e o caixa viravam massa de teste. Na PRIMEIRA carga (pull sem cursor)
/// só desce o que o aparelho precisa: o que está em aberto, de qualquer data, e o encerrado dentro da janela
/// de <see cref="SyncReversePullService.JanelaPrimeiraCargaDias"/> dias; o pedido cancelado não desce.
/// O pedido entregue conta a janela da entrega (EntreguEm, ou CriadoEm quando nula) e, enquanto tiver saldo a
/// receber, desce de qualquer idade: dinheiro a receber não some da tela.
/// O pull incremental não muda: o aparelho pode ter o registro e precisa saber do cancelamento e do estorno.
/// </summary>
public sealed class SyncReversePullPrimeiraCargaTests : IDisposable
{
    /// <summary>Cursor zerado: aparelho novo, sem nada do servidor.</summary>
    private static readonly DateTime SemCursor = DateTime.UnixEpoch;

    private static DateTime DentroDaJanela => DateTime.UtcNow.AddDays(-SyncReversePullService.JanelaPrimeiraCargaDias / 2.0);
    private static DateTime ForaDaJanela => DateTime.UtcNow.AddDays(-(SyncReversePullService.JanelaPrimeiraCargaDias + 20));

    private readonly EasyStockDbContext _db;
    private readonly SyncReversePullService _servico;
    private readonly Guid _empresaId = Guid.NewGuid();

    public SyncReversePullPrimeiraCargaTests()
    {
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.EmpresaId.Returns(_empresaId);
        _db = new EasyStockDbContext(
            new DbContextOptionsBuilder<EasyStockDbContext>()
                .UseInMemoryDatabase($"reverse-pull-primeira-carga-{Guid.NewGuid()}")
                .Options,
            currentUser);
        _servico = new SyncReversePullService(
            _db, new ConfigurationBuilder().Build(), NullLogger<SyncReversePullService>.Instance,
            new NumeroCurtoDosLotesMobile(_db, Substitute.For<EasyStock.Application.Ports.Output.Persistence.ILoteNumeroCurtoRepository>(), NullLogger<NumeroCurtoDosLotesMobile>.Instance));
    }

    public void Dispose() => _db.Dispose();

    private static DateTime HaDias(double dias) => DateTime.UtcNow.AddDays(-dias);

    /// <summary>O dobro da janela (60 dias com a janela de 30): longe o bastante para não haver dúvida de borda.</summary>
    private static DateTime MuitoAntes => HaDias(SyncReversePullService.JanelaPrimeiraCargaDias * 2);

    /// <summary>
    /// Pedido de 30,00 com um item. <paramref name="entreguEm"/> é a data da entrega (nula, como nos pedidos
    /// antigos); <paramref name="quitado"/> registra o pagamento do total, então o a receber cai a zero.
    /// </summary>
    private Pedido NovoPedido(string status, DateTime criadoEm, DateTime? alteradoEm = null, DateTime? entreguEm = null, bool quitado = false)
    {
        var pedido = Pedido.Criar(_empresaId, cliente: null, lojaId: null, "console");
        pedido.Itens.Add(new PedidoItem
        {
            Id = Guid.NewGuid(), PedidoId = pedido.Id, ProdutoId = Guid.NewGuid(), Nome = "Nhoque", Quantidade = 1m,
            PrecoUnitario = 30m, Subtotal = 30m, CriadoEm = criadoEm
        });
        pedido.RecalcularTotal();
        pedido.Status = status;
        pedido.CriadoEm = criadoEm;
        pedido.AlteradoEm = alteradoEm ?? criadoEm;
        pedido.EntreguEm = entreguEm;
        if (quitado)
            pedido.Pagamentos.Add(new PedidoPagamento
            {
                Id = Guid.NewGuid(), PedidoId = pedido.Id, Metodo = "pix", Valor = pedido.Total.Valor, PagoEm = criadoEm
            });
        _db.Add(pedido);
        return pedido;
    }

    private Lote NovoLote(string status, DateTime dataProducao, DateTime? alteradoEm = null)
    {
        var lote = Lote.Criar(_empresaId, "LOT-" + Guid.NewGuid().ToString("N")[..8], dataProducao);
        lote.Status = status;
        lote.NumeroCurto = "00000001";
        lote.CriadoEm = dataProducao;
        lote.AlteradoEm = alteradoEm ?? dataProducao;
        _db.Add(lote);
        return lote;
    }

    private MovimentoCaixa NovoLancamento(DateTime dataMovimento, DateTime? criadoEm = null, DateTime? estornadoEm = null)
    {
        var lancamento = MovimentoCaixa.Criar(_empresaId, "saida", 25m, dataMovimento);
        lancamento.Descricao = "Embalagem";
        lancamento.CriadoEm = criadoEm ?? dataMovimento;
        lancamento.EstornadoEm = estornadoEm;
        _db.Add(lancamento);
        return lancamento;
    }

    private FechamentoCaixa NovoFechamento(DateTime fechadoEm, DateTime? dia = null)
    {
        var fechamento = FechamentoCaixa.Criar(_empresaId, DateOnly.FromDateTime(dia ?? fechadoEm), 0m, 0m, 100m, 0m, 20m);
        fechamento.FechadoEm = fechadoEm;
        _db.Add(fechamento);
        return fechamento;
    }

    private async Task<List<MutationDto>> PullAsync(DateTime sinceDate)
    {
        await _db.SaveChangesAsync();
        var mutations = new List<MutationDto>();
        await _servico.AppendAsync(mutations, sinceDate, _empresaId, null);
        return mutations;
    }

    private static List<string> Ids(IEnumerable<MutationDto> mutations, string tipo) => mutations
        .Where(m => m.Type == tipo)
        .Select(m => m.Payload.GetProperty("id").GetString()!)
        .ToList();

    private static MutationDto Mutation(IEnumerable<MutationDto> mutations, string tipo, Guid id) => mutations
        .Single(m => m.Type == tipo && m.Payload.GetProperty("id").GetString() == id.ToString());

    // ── Pedido ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PrimeiraCarga_PedidoCancelado_NaoDesce()
    {
        var cancelado = NovoPedido("cancelado", DentroDaJanela);
        var canceladoAntigo = NovoPedido("cancelado", ForaDaJanela);
        var emAberto = NovoPedido("aguardando", DentroDaJanela);

        var mutations = await PullAsync(SemCursor);

        Ids(mutations, "order.upsert").Should()
            .NotContain(new[] { cancelado.Id.ToString(), canceladoAntigo.Id.ToString() },
                "o cancelado é histórico: o aparelho novo não precisa dele, de qualquer idade e mesmo com saldo a receber")
            .And.Contain(emAberto.Id.ToString());
    }

    [Fact]
    public async Task PrimeiraCarga_PedidoEntregueForaDaJanelaEQuitado_NaoDesce()
    {
        var antigo = NovoPedido("entregue", MuitoAntes, entreguEm: MuitoAntes, quitado: true);
        var emAberto = NovoPedido("aguardando", DentroDaJanela);

        var mutations = await PullAsync(SemCursor);

        Ids(mutations, "order.upsert").Should()
            .NotContain(antigo.Id.ToString(), "o pedido encerrado fora da janela e sem saldo a receber é histórico")
            .And.Contain(emAberto.Id.ToString());
    }

    /// <summary>Dinheiro a receber não pode sumir da tela: o entregue com saldo desce de qualquer idade.</summary>
    [Fact]
    public async Task PrimeiraCarga_PedidoEntregueForaDaJanelaComSaldoAReceber_Desce()
    {
        var devedor = NovoPedido("entregue", MuitoAntes, entreguEm: MuitoAntes);

        var mutations = await PullAsync(SemCursor);

        Ids(mutations, "order.upsert").Should().ContainSingle().Which.Should().Be(devedor.Id.ToString(),
            "o pedido entregue ainda com saldo a receber não pode sumir do aparelho novo, qualquer que seja a idade");
    }

    /// <summary>A janela conta da entrega: um dia dentro dela o quitado desce, um dia fora não desce.</summary>
    [Theory]
    [InlineData(SyncReversePullService.JanelaPrimeiraCargaDias - 1, true)]
    [InlineData(SyncReversePullService.JanelaPrimeiraCargaDias + 1, false)]
    public async Task PrimeiraCarga_PedidoEntregueQuitado_BordaDaJanelaContaDaEntrega(int diasDesdeAEntrega, bool desce)
    {
        // Criado 10 dias antes de ser entregue: nos dois casos a criação cai fora da janela, só a entrega decide.
        var entregue = NovoPedido("entregue", HaDias(diasDesdeAEntrega + 10), entreguEm: HaDias(diasDesdeAEntrega), quitado: true);

        var mutations = await PullAsync(SemCursor);

        Ids(mutations, "order.upsert").Contains(entregue.Id.ToString()).Should().Be(desce,
            "a janela é de {0} dias, contada da entrega", SyncReversePullService.JanelaPrimeiraCargaDias);
    }

    /// <summary>A janela conta da entrega, não da criação: criado há 50 dias e entregue ontem desce.</summary>
    [Fact]
    public async Task PrimeiraCarga_PedidoCriadoHaMuitoTempoEEntregueOntem_Desce()
    {
        var entregueOntem = NovoPedido("entregue", ForaDaJanela, entreguEm: HaDias(1), quitado: true);

        var mutations = await PullAsync(SemCursor);

        Ids(mutations, "order.upsert").Should().ContainSingle().Which.Should().Be(entregueOntem.Id.ToString(),
            "a entrega é de ontem, dentro da janela, ainda que o pedido tenha sido criado fora dela");
    }

    /// <summary>Pedido sem EntreguEm (dado antigo): a janela conta do CriadoEm.</summary>
    [Theory]
    [InlineData(SyncReversePullService.JanelaPrimeiraCargaDias * 2, false)]
    [InlineData(SyncReversePullService.JanelaPrimeiraCargaDias / 3, true)]
    public async Task PrimeiraCarga_PedidoEntregueSemDataDeEntrega_ContaDoCriadoEm(int diasDesdeACriacao, bool desce)
    {
        var entregue = NovoPedido("entregue", HaDias(diasDesdeACriacao), entreguEm: null, quitado: true);

        var mutations = await PullAsync(SemCursor);

        Ids(mutations, "order.upsert").Contains(entregue.Id.ToString()).Should().Be(desce,
            "sem EntreguEm a janela de {0} dias conta do CriadoEm", SyncReversePullService.JanelaPrimeiraCargaDias);
    }

    [Fact]
    public async Task PrimeiraCarga_PedidoEntregueDentroDaJanela_Desce()
    {
        var recente = NovoPedido("entregue", DentroDaJanela);

        var mutations = await PullAsync(SemCursor);

        Ids(mutations, "order.upsert").Should().ContainSingle().Which.Should().Be(recente.Id.ToString());
    }

    [Theory]
    [InlineData("aguardando")]
    [InlineData("preparando")]
    [InlineData("pronto")]
    public async Task PrimeiraCarga_PedidoEmAbertoAntigo_DesceDeQualquerData(string status)
    {
        var antigo = NovoPedido(status, ForaDaJanela);

        var mutations = await PullAsync(SemCursor);

        Ids(mutations, "order.upsert").Should().ContainSingle().Which.Should().Be(antigo.Id.ToString());
    }

    // ── Lote ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("finalizado")]
    [InlineData("expirado")]
    public async Task PrimeiraCarga_LoteEncerradoForaDaJanela_NaoDesce(string status)
    {
        var antigo = NovoLote(status, ForaDaJanela);
        var emProducao = NovoLote("em_producao", DentroDaJanela);

        var mutations = await PullAsync(SemCursor);

        Ids(mutations, "batch.upsert").Should()
            .NotContain(antigo.Id.ToString(), "o lote encerrado fora da janela é histórico")
            .And.Contain(emProducao.Id.ToString());
    }

    [Fact]
    public async Task PrimeiraCarga_LoteFinalizadoDentroDaJanela_Desce()
    {
        var recente = NovoLote("finalizado", DentroDaJanela);

        var mutations = await PullAsync(SemCursor);

        Ids(mutations, "batch.upsert").Should().ContainSingle().Which.Should().Be(recente.Id.ToString());
    }

    [Fact]
    public async Task PrimeiraCarga_LoteEmProducaoAntigo_DesceDeQualquerData()
    {
        var antigo = NovoLote("em_producao", ForaDaJanela);

        var mutations = await PullAsync(SemCursor);

        Ids(mutations, "batch.upsert").Should().ContainSingle().Which.Should().Be(antigo.Id.ToString());
    }

    // ── Caixa ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PrimeiraCarga_LancamentoForaDaJanela_NaoDesce()
    {
        var antigo = NovoLancamento(ForaDaJanela);
        var antigoEstornado = NovoLancamento(ForaDaJanela, estornadoEm: ForaDaJanela.AddHours(1));
        var recente = NovoLancamento(DentroDaJanela);

        var mutations = await PullAsync(SemCursor);

        Ids(mutations, "cashEntry.upsert").Should()
            .NotContain(new[] { antigo.Id.ToString(), antigoEstornado.Id.ToString() }, "o lançamento fora da janela é histórico, estornado ou não")
            .And.Contain(recente.Id.ToString());
    }

    [Fact]
    public async Task PrimeiraCarga_FechamentoForaDaJanela_NaoDesce()
    {
        var antigo = NovoFechamento(ForaDaJanela);
        var recente = NovoFechamento(DentroDaJanela);

        var mutations = await PullAsync(SemCursor);

        Ids(mutations, "closing.upsert").Should()
            .NotContain(antigo.Id.ToString(), "o fechamento fora da janela é histórico")
            .And.Contain(recente.Id.ToString());
    }

    // ── Pull incremental ──────────────────────────────────────────────────────

    /// <summary>
    /// Característica do comportamento atual, antes e depois do filtro: com cursor, o aparelho pode ter o
    /// registro e precisa saber do cancelamento e do estorno, então o cancelado e o antigo continuam descendo.
    /// O entregue antigo está quitado: com saldo ele desceria até se a janela vazasse para o incremental.
    /// </summary>
    [Fact]
    public async Task PullIncremental_ContinuaDevolvendoCanceladoEAntigo()
    {
        var cursor = DateTime.UtcNow.AddDays(-1);
        var agora = DateTime.UtcNow;
        var cancelado = NovoPedido("cancelado", ForaDaJanela, alteradoEm: agora);
        var canceladoRecente = NovoPedido("cancelado", DentroDaJanela, alteradoEm: agora);
        var entregueAntigo = NovoPedido("entregue", ForaDaJanela, alteradoEm: agora, entreguEm: ForaDaJanela, quitado: true);
        var loteAntigo = NovoLote("finalizado", ForaDaJanela, alteradoEm: agora);
        var lancamentoRetroativo = NovoLancamento(ForaDaJanela, criadoEm: agora);
        var lancamentoEstornado = NovoLancamento(ForaDaJanela, estornadoEm: agora);
        var fechamentoTardio = NovoFechamento(fechadoEm: agora, dia: ForaDaJanela);

        var mutations = await PullAsync(cursor);

        Ids(mutations, "order.upsert").Should().BeEquivalentTo(new[] { cancelado.Id.ToString(), canceladoRecente.Id.ToString(), entregueAntigo.Id.ToString() });
        Ids(mutations, "batch.upsert").Should().BeEquivalentTo(new[] { loteAntigo.Id.ToString() });
        Ids(mutations, "cashEntry.upsert").Should().BeEquivalentTo(new[] { lancamentoRetroativo.Id.ToString(), lancamentoEstornado.Id.ToString() });
        Ids(mutations, "closing.upsert").Should().BeEquivalentTo(new[] { fechamentoTardio.Id.ToString() });
        Mutation(mutations, "order.upsert", cancelado.Id).Payload.GetProperty("status").GetString()
            .Should().Be("cancelado", "o aparelho que já tinha o pedido precisa saber do cancelamento");
        Mutation(mutations, "cashEntry.upsert", lancamentoEstornado.Id).Payload.GetProperty("estornado").GetBoolean()
            .Should().BeTrue("o aparelho que já tinha o lançamento precisa saber do estorno");
    }
}

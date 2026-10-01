using EasyStock.Application.Ports.Output.Persistence.Operacao;
using EasyStock.Application.UseCases.Operacao.Impressao;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Tests.UseCases.Operacao.Impressao;

/// <summary>
/// S49 (#1269): impresso de expedição do pedido. Agendado mostra janela e "pronto até" 30 min antes;
/// imediato prevê pronto pelo tempo de preparo e saída 30 min depois; o total diz se cobra ou se já foi pago.
/// Horários de entrada em UTC, de saída em Brasília (UTC−3).
/// </summary>
public class MontarPedidoImpressoUseCaseTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid PedidoId = Guid.Parse("a7f3c21b-0000-4000-8000-000000000001");
    private static readonly Guid ClienteId = Guid.Parse("0412abcd-0000-4000-8000-000000000002");
    private static readonly DateTime Agora = new(2026, 10, 2, 10, 40, 0, DateTimeKind.Utc);

    private sealed class RelogioFixo(DateTime utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utc, TimeSpan.Zero);
    }

    private static PedidoImpressoLeitura Leitura(
        PedidoImpressoJanelaLeitura? janela = null,
        DateTime? agendadoParaEm = null,
        IReadOnlyList<PedidoImpressoPagamentoLeitura>? pagamentos = null,
        string? formaCobranca = "pix",
        PedidoImpressoEntregaLeitura? entrega = null,
        IReadOnlyList<PedidoImpressoItemLeitura>? itens = null) =>
        new(
            Id: PedidoId,
            CriadoEm: new DateTime(2026, 9, 30, 17, 32, 0, DateTimeKind.Utc),
            AlteradoEm: new DateTime(2026, 10, 1, 12, 12, 0, DateTimeKind.Utc),
            AgendadoParaEm: agendadoParaEm,
            Janela: janela,
            Casa: new PedidoImpressoCasaLeitura("Casa da Baba", "12345678000190", "casadababa.com.br", "11900000000", null),
            Cliente: new PedidoImpressoClienteLeitura(
                ClienteId, " Mariana Souza ", "+5511987654412", "Rua das Laranjeiras, 214", null, "ap 32", "Vila Mariana", "São Paulo", "04000000"),
            Observacoes: "Deixar na portaria",
            Total: 173.30m,
            Pagamentos: pagamentos ?? [],
            FormaCobranca: formaCobranca,
            Entrega: entrega,
            TempoPreparoPadraoMinutos: 45,
            Itens: itens ??
            [
                new PedidoImpressoItemLeitura("Pão de queijo recheado", "500 g", 2, null, 24.90m, 49.80m, null, EhProduto: true),
                new PedidoImpressoItemLeitura("Bolo de cenoura", null, 1, "UN", 42m, 42m, " sem granulado ", EhProduto: true),
            ]);

    private static async Task<PedidoImpressoDto?> Montar(PedidoImpressoLeitura? leitura, string? nota = null)
    {
        var queries = Substitute.For<IPedidoImpressoQueries>();
        queries.ObterAsync(EmpresaId, PedidoId, Arg.Any<CancellationToken>()).Returns(leitura);
        var uc = new MontarPedidoImpressoUseCase(queries, new RelogioFixo(Agora));
        return await uc.ExecuteAsync(new MontarPedidoImpressoInput(EmpresaId, PedidoId, nota));
    }

    [Fact]
    public async Task AgendadoCalculaProntoAte()
    {
        var dto = await Montar(Leitura(janela: new PedidoImpressoJanelaLeitura(new DateOnly(2026, 10, 2), new TimeOnly(10, 0), new TimeOnly(11, 0))));

        dto!.Prazo.Agendado.Should().BeTrue();
        dto.Prazo.Entrega.Should().Be(new DateTime(2026, 10, 2, 10, 0, 0));
        dto.Prazo.EntregaFim.Should().Be(new DateTime(2026, 10, 2, 11, 0, 0));
        dto.Prazo.ProntoAte.Should().Be(new DateTime(2026, 10, 2, 9, 30, 0));
        dto.SolicitadoEm.Should().Be(new DateTime(2026, 9, 30, 14, 32, 0));
        dto.AlteradoEm.Should().Be(new DateTime(2026, 10, 1, 9, 12, 0));
        dto.ImpressoEm.Should().Be(new DateTime(2026, 10, 2, 7, 40, 0));
    }

    [Fact]
    public async Task AgendadoSemVagaUsaOHorarioAgendado()
    {
        var dto = await Montar(Leitura(agendadoParaEm: new DateTime(2026, 10, 2, 15, 0, 0, DateTimeKind.Utc)));

        dto!.Prazo.Agendado.Should().BeTrue();
        dto.Prazo.Entrega.Should().Be(new DateTime(2026, 10, 2, 12, 0, 0));
        dto.Prazo.EntregaFim.Should().BeNull();
        dto.Prazo.ProntoAte.Should().Be(new DateTime(2026, 10, 2, 11, 30, 0));
    }

    [Fact]
    public async Task ImediatoPreveProntoESaida()
    {
        var pagoEm = new DateTime(2026, 10, 2, 17, 25, 0, DateTimeKind.Utc);
        var dto = await Montar(Leitura(pagamentos: [new PedidoImpressoPagamentoLeitura(173.30m, pagoEm, "pix")]));

        dto!.Prazo.Agendado.Should().BeFalse();
        dto.Prazo.ProntoAte.Should().Be(new DateTime(2026, 10, 2, 15, 10, 0), "pago 14:25 + 45 min de preparo");
        dto.Prazo.Entrega.Should().Be(new DateTime(2026, 10, 2, 15, 40, 0), "pronto + 30 min");
    }

    [Fact]
    public async Task ImediatoSemPagamentoContaDaCriacao()
    {
        var dto = await Montar(Leitura());

        dto!.Prazo.ProntoAte.Should().Be(new DateTime(2026, 9, 30, 15, 17, 0));
    }

    [Fact]
    public async Task PagoVersusCobrarNaEntrega()
    {
        var pendente = await Montar(Leitura(pagamentos: [new PedidoImpressoPagamentoLeitura(50m, Agora, "dinheiro")]));
        pendente!.Cobranca.Pago.Should().BeFalse();
        pendente.Cobranca.Forma.Should().Be("pix", "a forma da cobrança vence o método do pagamento parcial");
        pendente.Cobranca.Total.Should().Be(173.30m);

        var pago = await Montar(Leitura(formaCobranca: null, pagamentos:
        [
            new PedidoImpressoPagamentoLeitura(100m, Agora.AddMinutes(-5), "pix"),
            new PedidoImpressoPagamentoLeitura(73.30m, Agora, "cartao"),
        ]));
        pago!.Cobranca.Pago.Should().BeTrue();
        pago.Cobranca.Forma.Should().Be("cartao", "sem cobrança, vale o último pagamento");
    }

    [Fact]
    public async Task ClienteComTelefoneCompletoIdCurtoEEndereco()
    {
        var dto = await Montar(Leitura());

        dto!.Numero.Should().Be("A7F3C21B");
        dto.Cliente.IdCurto.Should().Be("0412AB");
        dto.Cliente.Nome.Should().Be("Mariana Souza");
        dto.Cliente.Telefone.Should().Be("+5511987654412");
        dto.Cliente.Endereco.Should().Be("Rua das Laranjeiras, 214, ap 32 - Vila Mariana, São Paulo - CEP 04000-000");
    }

    [Fact]
    public async Task ItensComVariacaoObservacaoEQuantidadeTotal()
    {
        var dto = await Montar(Leitura());

        dto!.Itens.Select(i => i.Nome).Should().Equal("Pão de queijo recheado (500 g)", "Bolo de cenoura");
        dto.Itens[1].Observacao.Should().Be("sem granulado");
        dto.QuantidadeItens.Should().Be(3);
        dto.Itens.Select(i => i.Unidade).Should().Equal("un", "un");
        dto.Observacao.Should().Be("Deixar na portaria");
    }

    [Fact]
    public async Task FreteNaoContaEPesoContaUm()
    {
        var dto = await Montar(Leitura(itens:
        [
            new PedidoImpressoItemLeitura("Coxinha", null, 3, "un", 8.5m, 25.5m, null, EhProduto: true),
            new PedidoImpressoItemLeitura("Massa fresca", null, 0.5m, "kg", 60m, 30m, null, EhProduto: true),
            new PedidoImpressoItemLeitura("Molho", null, 1.5m, null, 10m, 15m, null, EhProduto: true),
            new PedidoImpressoItemLeitura("Frete", null, 1, null, 12m, 12m, null, EhProduto: false),
        ]));

        dto!.QuantidadeItens.Should().Be(5, "3 coxinhas + 1 massa a peso + 1 molho fracionado; frete fora");
        dto.Itens.Select(i => i.Unidade).Should().Equal("un", "kg", "un", "un");
        dto.Itens.Should().HaveCount(4, "o frete continua impresso como linha");
    }

    [Fact]
    public async Task EntregaComTipoEResponsavel()
    {
        var dto = await Montar(Leitura(entrega: new PedidoImpressoEntregaLeitura(TipoEntregador.Motoboy, "João")));

        dto!.Entrega.Should().Be(new PedidoImpressoEntregaDto(TipoEntregador.Motoboy, "João"));
        (await Montar(Leitura()))!.Entrega.Should().BeNull();
    }

    [Fact]
    public async Task NotaCortadaNoLimite()
    {
        var dto = await Montar(Leitura(), "  " + new string('x', 100));

        dto!.Nota.Should().HaveLength(MontarPedidoImpressoUseCase.NotaTamanhoMaximo);
        (await Montar(Leitura(), "   "))!.Nota.Should().BeNull();
    }

    [Fact]
    public async Task OutraEmpresaNull()
    {
        (await Montar(null)).Should().BeNull();
    }
}

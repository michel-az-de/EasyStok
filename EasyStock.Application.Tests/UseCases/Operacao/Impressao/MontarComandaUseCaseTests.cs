using EasyStock.Application.Ports.Output.Persistence.Operacao;
using EasyStock.Application.UseCases.Operacao.Impressao;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Tests.UseCases.Operacao.Impressao;

/// <summary>
/// S52 (#1281): comanda de cozinha. Grupos na ordem preparar em casa, para servir, outros; frete fora; alergia
/// vem das tags <c>alergia_*</c> do cadastro e vale para o pedido inteiro.
/// </summary>
public class MontarComandaUseCaseTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid PedidoId = Guid.Parse("a7f3c21b-0000-4000-8000-000000000001");
    private static readonly DateTime Agora = new(2026, 10, 2, 10, 40, 0, DateTimeKind.Utc);

    private sealed class RelogioFixo(DateTime utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utc, TimeSpan.Zero);
    }

    private static PedidoImpressoItemLeitura Item(
        string nome, decimal qtd, string? linha, bool produto = true, string? unidade = null,
        string? variacao = null, string? molho = null, string? obs = null) =>
        new(nome, variacao, qtd, unidade, 10m, 10m * qtd, obs, produto, linha, molho);

    private static PedidoImpressoLeitura Leitura(IReadOnlyList<PedidoImpressoItemLeitura> itens, IReadOnlyList<string>? tags = null) =>
        new(
            Id: PedidoId,
            CriadoEm: new DateTime(2026, 9, 30, 17, 32, 0, DateTimeKind.Utc),
            AlteradoEm: new DateTime(2026, 10, 1, 12, 12, 0, DateTimeKind.Utc),
            AgendadoParaEm: null,
            Janela: new PedidoImpressoJanelaLeitura(new DateOnly(2026, 10, 2), new TimeOnly(10, 0), new TimeOnly(11, 0)),
            Casa: new PedidoImpressoCasaLeitura("Casa da Baba", null, null, null, null),
            Cliente: new PedidoImpressoClienteLeitura(Guid.NewGuid(), " Mariana Souza ", "+5511987654412", "Rua X", null, null, null, null, null, tags),
            Observacoes: " Embalar os pães separados ",
            Total: 100m,
            Pagamentos: [],
            FormaCobranca: null,
            Entrega: new PedidoImpressoEntregaLeitura(TipoEntregador.Motoboy, "João"),
            TempoPreparoPadraoMinutos: 60,
            Itens: itens);

    private static async Task<ComandaDto?> Montar(PedidoImpressoLeitura? leitura)
    {
        var queries = Substitute.For<IPedidoImpressoQueries>();
        queries.ObterAsync(EmpresaId, PedidoId, Arg.Any<CancellationToken>()).Returns(leitura);
        return await new MontarComandaUseCase(queries, new RelogioFixo(Agora))
            .ExecuteAsync(new MontarComandaInput(EmpresaId, PedidoId));
    }

    [Fact]
    public async Task AgrupaPorLinhaNaOrdem()
    {
        var dto = await Montar(Leitura(
        [
            Item("Talharim ao pesto", 1, "paraServir", variacao: "400 g"),
            Item("Nhoque de batata", 2, "prepararEmCasa", variacao: "800 g", molho: "sugo"),
            Item("Brinde", 1, null),
            Item("Massa fresca", 0.5m, "prepararEmCasa", unidade: "KG", obs: " sem sal "),
        ]));

        dto!.Grupos.Select(g => g.Titulo).Should().Equal("Preparar em casa", "Para servir", "Outros");
        dto.Grupos[0].Itens.Select(i => i.Nome).Should().Equal("Nhoque de batata", "Massa fresca");
        dto.Grupos[0].Itens[0].Should().Be(new ComandaItemDto(2, "un", "Nhoque de batata", "800 g", "sugo", null));
        dto.Grupos[0].Itens[1].Should().Be(new ComandaItemDto(0.5m, "kg", "Massa fresca", null, null, "sem sal"));
        dto.TotalLinhas.Should().Be(4);
    }

    [Fact]
    public async Task FreteFora()
    {
        var dto = await Montar(Leitura([Item("Nhoque", 1, "prepararEmCasa"), Item("Frete", 1, null, produto: false)]));

        dto!.Grupos.SelectMany(g => g.Itens).Select(i => i.Nome).Should().Equal("Nhoque");
    }

    [Fact]
    public async Task AlergiaDoCadastro()
    {
        var dto = await Montar(Leitura([Item("Nhoque", 1, "prepararEmCasa")],
            ["alergia_frutos_do_mar", "vegano", "alergia_castanha", "alergia_", "ALERGIA_castanha"]));

        dto!.Alergias.Should().Equal("CASTANHA", "FRUTOS DO MAR");
        (await Montar(Leitura([Item("Nhoque", 1, "prepararEmCasa")], ["sem_gluten"])))!.Alergias.Should().BeEmpty();
    }

    [Fact]
    public async Task CabecalhoComPrazoClienteEEntrega()
    {
        var dto = await Montar(Leitura([Item("Nhoque", 1, "prepararEmCasa")]));

        dto!.Numero.Should().Be("A7F3C21B");
        dto.NumeroDoDia.Should().BeNull("o número do dia chega na S53");
        dto.Prazo.ProntoAte.Should().Be(new DateTime(2026, 10, 2, 9, 30, 0));
        dto.Cliente.Should().Be("Mariana Souza");
        dto.Entrega.Should().Be(new PedidoImpressoEntregaDto(TipoEntregador.Motoboy, "João"));
        dto.Observacao.Should().Be("Embalar os pães separados");
        dto.ImpressoEm.Should().Be(new DateTime(2026, 10, 2, 7, 40, 0));
    }

    [Fact]
    public async Task OutraEmpresaNull()
    {
        (await Montar(null)).Should().BeNull();
    }
}

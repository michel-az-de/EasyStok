using EasyStock.Api.Services.Impressao;
using EasyStock.Application.UseCases.Operacao.Impressao;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Infra.Notifications.Templating;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Api.UnitTests.Services.Impressao;

/// <summary>
/// S52 e #1632: o canhoto de cozinha no layout "bilhete da casa"
/// (docs/plan/atendimento-whatsapp/impressos/impressos-v4-proposta.html), em qualquer papel.
/// Snapshots em <c>Snapshots/</c>; regravar com <c>IMPRESSAO_ATUALIZAR_SNAPSHOT=1</c> e revisar o diff.
/// </summary>
public class ComandaHtmlTests
{
    private const string BaseUrl = "https://api.test";

    public static TheoryData<string> Papeis => new(ImpressoHtml.Papeis);

    private static ComandaDto Exemplo(
        int? numeroDoDia = 42, IReadOnlyList<string>? alergias = null, string cliente = "Mariana Souza",
        string? observacao = "Embalar os pães separados", bool agendado = true) => new(
        "A7F3C21B",
        numeroDoDia,
        agendado
            ? new PedidoImpressoPrazoDto(true, new DateTime(2026, 10, 2, 10, 0, 0), new DateTime(2026, 10, 2, 11, 0, 0), new DateTime(2026, 10, 2, 9, 30, 0))
            : new PedidoImpressoPrazoDto(false, new DateTime(2026, 10, 2, 15, 10, 0), null, new DateTime(2026, 10, 2, 14, 40, 0)),
        cliente,
        new PedidoImpressoEntregaDto(TipoEntregador.Motoboy, null),
        alergias ?? ["CASTANHA"],
        [
            new ComandaGrupoDto("prepararEmCasa", "Preparar em casa",
            [
                new ComandaItemDto(2, "un", "Nhoque de batata", "800 g", "sugo", null),
                new ComandaItemDto(1, "un", "Lasanha à bolonhesa", "Família", null, "cortar em 6 pedaços"),
                new ComandaItemDto(0.5m, "kg", "Massa fresca de espinafre", null, "manteiga e sálvia", null),
            ]),
            new ComandaGrupoDto("paraServir", "Para servir",
            [
                new ComandaItemDto(1, "un", "Talharim ao pesto", "400 g", null, null),
                new ComandaItemDto(3, "un", "Pão de queijo recheado", null, null, null),
            ]),
        ],
        observacao,
        new DateTime(2026, 9, 30, 14, 32, 0),
        new DateTime(2026, 10, 2, 7, 40, 0));

    private static Task<string> Renderizar(ComandaDto dto, string modelo) =>
        new ComandaHtml(new ScribanRenderer(NullLogger<ScribanRenderer>.Instance)).RenderizarAsync(dto, modelo, BaseUrl);

    [Theory]
    [InlineData(ImpressoHtml.Cupom80)]
    [InlineData(ImpressoHtml.A5)]
    public async Task Snapshot(string modelo)
    {
        SnapshotImpresso.Conferir($"comanda.{modelo}.html", await Renderizar(Exemplo(), modelo));
    }

    [Theory]
    [MemberData(nameof(Papeis))]
    public async Task MostraOQueACozinhaPrecisa(string modelo)
    {
        var corpo = SnapshotImpresso.Corpo(await Renderizar(Exemplo(), modelo));

        corpo.Should().ContainAll("ALERGIA: CASTANHA", "confira todos os itens", "<b>042</b>", "Pronto até · sex 02/10", "<b>09:30</b>");
        corpo.Should().ContainAll("<span class=\"selo\">AGENDADO</span>", "Mariana Souza", "entrega 10–11h", "Motoboy", "5 itens");
        corpo.Should().ContainAll("Preparar em casa", "3 itens", "Para servir", "2 itens", "Nhoque de batata", "· 800 g", "MOLHO: SUGO");
        corpo.Should().ContainAll("OBS: cortar em 6 pedaços", "0,5 kg", "<i class=\"chk\"></i>", "Feito por", "Embalado por");
        corpo.Should().ContainAll("Observação do pedido", "Embalar os pães separados", "<svg class=\"qr\"", "#A7F3C21B", "Solicitado 30/09 14:32");
    }

    [Theory]
    [MemberData(nameof(Papeis))]
    public async Task PapelDeProducaoSemPrecoEnderecoNemMarca(string modelo)
    {
        var corpo = SnapshotImpresso.Corpo(await Renderizar(Exemplo(), modelo));

        corpo.Should().NotContainAny("R$", "PIX", "Telefone", "CNPJ", "class=\"logo\"", "class=\"wm\"", "class=\"fecho\"");
    }

    [Fact]
    public async Task SemAlergiaSemFaixaESemNumeroDoDiaMostraOCodigo()
    {
        var corpo = SnapshotImpresso.Corpo(await Renderizar(Exemplo(numeroDoDia: null, alergias: [], observacao: null), ImpressoHtml.Cupom80));

        corpo.Should().NotContain("class=\"alerta\"").And.NotContain("Observação do pedido");
        corpo.Should().Contain("<b class=\"cod\">A7F3C21B</b>");
    }

    [Fact]
    public async Task ImediatoLevaOSeloEAHoraDeHoje()
    {
        var corpo = SnapshotImpresso.Corpo(await Renderizar(Exemplo(agendado: false), ImpressoHtml.Cupom58));

        corpo.Should().ContainAll("<span class=\"selo\">IMEDIATO</span>", "Pronto até · hoje", "<b>14:40</b>");
    }

    [Theory]
    [MemberData(nameof(Papeis))]
    public async Task EscapaTexto(string modelo)
    {
        var corpo = SnapshotImpresso.Corpo(await Renderizar(Exemplo(cliente: "<script>x</script>", observacao: "<B>obs</B>"), modelo));

        corpo.Should().NotContain("<script>").And.NotContain("<B>");
        corpo.Should().Contain("&lt;script&gt;");
    }

    [Theory]
    [InlineData(ImpressoHtml.Etiqueta10x15, "100mm 150mm")]
    [InlineData(ImpressoHtml.Cupom80, "80mm auto")]
    [InlineData(ImpressoHtml.Cupom58, "58mm auto")]
    public async Task TermicaSoPretoENoTamanhoDoPapel(string modelo, string pagina)
    {
        var html = await Renderizar(Exemplo(), modelo);

        SnapshotImpresso.CoresDoCorpo(html).Should().OnlyContain(c => c == "#000", "a térmica só imprime preto");
        html.Should().Contain($"@page{{size:{pagina};margin:0}}");
    }

    [Fact]
    public async Task ModeloDesconhecidoLanca()
    {
        var act = () => Renderizar(Exemplo(), "carta");

        await act.Should().ThrowAsync<ArgumentException>();
    }
}

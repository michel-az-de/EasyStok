using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using EasyStock.Api.Services.Impressao;
using EasyStock.Application.UseCases.Operacao.Impressao;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Infra.Notifications.Templating;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Api.UnitTests.Services.Impressao;

/// <summary>
/// S52 (#1281): a comanda sai igual ao layout aprovado (docs/plan/atendimento-whatsapp/impressos/comanda-aprovada.html).
/// Snapshots em <c>Snapshots/</c>; regravar com <c>IMPRESSAO_ATUALIZAR_SNAPSHOT=1</c> e revisar o diff.
/// </summary>
public class ComandaHtmlTests
{
    private const string BaseUrl = "https://api.test";

    private static ComandaDto Exemplo(
        int? numeroDoDia = 42, IReadOnlyList<string>? alergias = null, string cliente = "Mariana Souza", string? observacao = "Embalar os pães separados") => new(
        "A7F3C21B",
        numeroDoDia,
        new PedidoImpressoPrazoDto(true, new DateTime(2026, 10, 2, 10, 0, 0), new DateTime(2026, 10, 2, 11, 0, 0), new DateTime(2026, 10, 2, 9, 30, 0)),
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

    private static string Corpo(string html) =>
        html[(html.IndexOf("<body>", StringComparison.Ordinal) + 6)..html.IndexOf("</body>", StringComparison.Ordinal)];

    [Theory]
    [InlineData(ImpressoHtml.Etiqueta10x15)]
    [InlineData(ImpressoHtml.Cupom58)]
    public async Task Snapshot(string modelo)
    {
        ConferirSnapshot($"comanda.{modelo}.html", await Renderizar(Exemplo(), modelo));
    }

    [Theory]
    [InlineData(ImpressoHtml.Etiqueta10x15)]
    [InlineData(ImpressoHtml.Cupom58)]
    public async Task MostraOQueACozinhaPrecisa(string modelo)
    {
        var corpo = Corpo(await Renderizar(Exemplo(), modelo));

        corpo.Should().ContainAll("ALERGIA: CASTANHA", "Comanda nº", "042", "#A7F3C21B", "AGENDADO", "Pronto até", "09:30",
            "sex 02/10", "10–11h", "Motoboy", "Mariana Souza", "Preparar em casa", "3 itens", "Para servir", "2 itens",
            "Nhoque de batata", "· 800 g", "molho: sugo", "OBS: cortar em 6 pedaços", "0,5", "kg", "Feito por", "Embalado por",
            "Embalar os pães separados", "5 itens", "Solicitado 30/09 14:32", "Impresso 02/10 07:40");
    }

    [Theory]
    [InlineData(ImpressoHtml.Etiqueta10x15)]
    [InlineData(ImpressoHtml.Cupom58)]
    public async Task SemPrecoNemEndereco(string modelo)
    {
        var corpo = Corpo(await Renderizar(Exemplo(), modelo));

        corpo.Should().NotContainAny("R$", "PIX", "Cobrar", "Telefone", "CNPJ");
    }

    [Fact]
    public async Task SemAlergiaSemFaixaESemNumeroDoDiaMostraOCodigo()
    {
        var corpo = Corpo(await Renderizar(Exemplo(numeroDoDia: null, alergias: [], observacao: null), ImpressoHtml.Etiqueta10x15));

        corpo.Should().NotContain("cm-alerta").And.NotContain("Comanda nº");
        corpo.Should().Contain("<span class=\"codigo\">#A7F3C21B</span>");
        corpo.Should().Contain("Observação do pedido</span>—");
    }

    [Theory]
    [InlineData(ImpressoHtml.Etiqueta10x15)]
    [InlineData(ImpressoHtml.Cupom58)]
    public async Task EscapaTexto(string modelo)
    {
        var corpo = Corpo(await Renderizar(Exemplo(cliente: "<script>alert(1)</script>", alergias: ["<B>"]), modelo));

        corpo.Should().NotContain("<script>").And.NotContain("<B>");
        corpo.Should().Contain("&lt;script&gt;");
    }

    [Theory]
    [InlineData(ImpressoHtml.Etiqueta10x15)]
    [InlineData(ImpressoHtml.Cupom58)]
    public async Task TermicoSoPreto(string modelo)
    {
        var html = await Renderizar(Exemplo(), modelo);
        var css = html[html.IndexOf("/* Comanda de cozinha", StringComparison.Ordinal)..html.IndexOf("</style>", StringComparison.Ordinal)];

        // Cor só em atributo ou CSS (fill="#000", color:#fff); "#A7F3C21B" no texto é o número.
        Regex.Matches(Corpo(html) + css, "[:=]\\s*\"?(#[0-9A-Fa-f]{3,6})\\b").Select(m => m.Groups[1].Value.ToLowerInvariant())
            .Should().OnlyContain(c => c == "#000" || c == "#fff", "a térmica só imprime preto");
        html.Should().Contain("@page{size:" + (modelo == ImpressoHtml.Cupom58 ? "58mm auto" : "100mm 150mm"));
    }

    [Fact]
    public async Task ModeloA4NaoExisteParaComanda()
    {
        var act = () => Renderizar(Exemplo(), ImpressoHtml.A4);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    private static void ConferirSnapshot(string nome, string atual, [CallerFilePath] string arquivoTeste = "")
    {
        var caminho = Path.Combine(Path.GetDirectoryName(arquivoTeste)!, "Snapshots", nome);
        if (Environment.GetEnvironmentVariable("IMPRESSAO_ATUALIZAR_SNAPSHOT") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
            File.WriteAllText(caminho, atual);
            return;
        }
        File.Exists(caminho).Should().BeTrue($"snapshot {nome} existe (gere com IMPRESSAO_ATUALIZAR_SNAPSHOT=1)");
        atual.ReplaceLineEndings().Should().Be(File.ReadAllText(caminho).ReplaceLineEndings());
    }
}

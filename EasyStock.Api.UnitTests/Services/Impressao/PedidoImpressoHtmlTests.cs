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
/// S49 (#1269): o impresso sai igual ao layout aprovado (docs/plan/atendimento-whatsapp/impressos/pedido-aprovado.html).
/// Snapshots em <c>Snapshots/</c>; para regravar depois de uma mudança aprovada, rodar com
/// <c>IMPRESSAO_ATUALIZAR_SNAPSHOT=1</c> e revisar o diff.
/// </summary>
public class PedidoImpressoHtmlTests
{
    private const string BaseUrl = "https://api.test";

    internal static PedidoImpressoDto Exemplo(bool agendado = true, string clienteNome = "Mariana Souza") => new(
        new PedidoImpressoCasaDto("Casa da Baba", "12345678000190", "https://casadababa.com.br/", "5511900000000", null),
        "A7F3C21B",
        agendado
            ? new PedidoImpressoPrazoDto(true, new DateTime(2026, 10, 2, 10, 0, 0), new DateTime(2026, 10, 2, 11, 0, 0), new DateTime(2026, 10, 2, 9, 30, 0))
            : new PedidoImpressoPrazoDto(false, new DateTime(2026, 10, 2, 15, 10, 0), null, new DateTime(2026, 10, 2, 14, 40, 0)),
        new PedidoImpressoClienteDto("0412AB", clienteNome, "+5511987654412", "Rua das Laranjeiras, 214, ap 32 - Vila Mariana, São Paulo"),
        new PedidoImpressoEntregaDto(TipoEntregador.Motoboy, "João"),
        [
            new PedidoImpressoItemDto(2, "Pão de queijo recheado 500 g", null, 24.90m, 49.80m),
            new PedidoImpressoItemDto(1, "Bolo de cenoura com cobertura de chocolate", "sem granulado", 42m, 42m),
            new PedidoImpressoItemDto(3, "Coxinha de frango congelada", null, 8.50m, 25.50m),
            new PedidoImpressoItemDto(1, "Torta de palmito média", null, 56m, 56m),
        ],
        "Deixar na portaria, bloco B",
        "Embalar os pães separados",
        new PedidoImpressoCobrancaDto(173.30m, false, "pix"),
        agendado ? new DateTime(2026, 9, 30, 14, 32, 0) : new DateTime(2026, 10, 2, 14, 32, 0),
        new DateTime(2026, 10, 1, 9, 12, 0),
        new DateTime(2026, 10, 2, 7, 40, 0));

    private static Task<string> Renderizar(PedidoImpressoDto dto, string modelo) =>
        new PedidoImpressoHtml(new ScribanRenderer(NullLogger<ScribanRenderer>.Instance)).RenderizarAsync(dto, modelo, BaseUrl);

    private static string Corpo(string html) =>
        html[(html.IndexOf("<body>", StringComparison.Ordinal) + 6)..html.IndexOf("</body>", StringComparison.Ordinal)];

    [Theory]
    [InlineData(PedidoImpressoHtml.Etiqueta10x15, true)]
    [InlineData(PedidoImpressoHtml.Etiqueta10x15, false)]
    [InlineData(PedidoImpressoHtml.Cupom58, true)]
    [InlineData(PedidoImpressoHtml.A4, true)]
    public async Task Snapshot(string modelo, bool agendado)
    {
        var html = await Renderizar(Exemplo(agendado), modelo);
        ConferirSnapshot($"pedido.{modelo}.{(agendado ? "agendado" : "imediato")}.html", html);
    }

    [Fact]
    public async Task AgendadoMostraJanelaEProntoAte()
    {
        var corpo = Corpo(await Renderizar(Exemplo(), PedidoImpressoHtml.Etiqueta10x15));

        corpo.Should().ContainAll("AGENDADO", "Entrega", "02/10", "Janela", "10–11h", "Pronto até", "09:30", "Solicitado", "30/09 14:32");
        corpo.Should().ContainAll("CNPJ 12.345.678/0001-90", "casadababa.com.br", "WhatsApp (11) 90000-0000");
        corpo.Should().ContainAll("(11) 98765-4412", "ID #0412AB", "Motoboy · João", "Cobrar na entrega · PIX · 7 itens", "R$ 173,30");
        corpo.Should().ContainAll("Alterado 01/10 09:12", "Impresso 02/10 07:40");
    }

    [Fact]
    public async Task ImediatoMostraPrevisao()
    {
        var corpo = Corpo(await Renderizar(Exemplo(agendado: false), PedidoImpressoHtml.Etiqueta10x15));

        corpo.Should().ContainAll("IMEDIATO", "Previsão", "hoje", "Sai às", "~15:10", "14:40", "hoje 14:32");
        corpo.Should().NotContain("AGENDADO");
    }

    [Fact]
    public async Task A4MostraUnitarioEDataLonga()
    {
        var corpo = Corpo(await Renderizar(Exemplo(), PedidoImpressoHtml.A4));

        corpo.Should().ContainAll("Unitário", "R$ 24,90", "sex 02/10", "10:00–11:00", "qua 30/09 14:32", "Casa da Baba");
    }

    [Theory]
    [InlineData(PedidoImpressoHtml.Etiqueta10x15)]
    [InlineData(PedidoImpressoHtml.Cupom58)]
    [InlineData(PedidoImpressoHtml.A4)]
    public async Task EscapaTextoDoCliente(string modelo)
    {
        var corpo = Corpo(await Renderizar(Exemplo(clienteNome: "<script>alert(1)</script>"), modelo));

        corpo.Should().NotContain("<script>");
        corpo.Should().Contain("&lt;script&gt;");
    }

    [Theory]
    [InlineData(PedidoImpressoHtml.Etiqueta10x15)]
    [InlineData(PedidoImpressoHtml.Cupom58)]
    public async Task TermicoSoPreto(string modelo)
    {
        var corpo = Corpo(await Renderizar(Exemplo(), modelo));

        corpo.Should().StartWith("<div class=\"t ");
        // Cor só em atributo ou CSS (fill="#000", color:#...); "#0412AB" no texto é o ID do cliente.
        Regex.Matches(corpo, "[:=]\\s*\"?(#[0-9A-Fa-f]{3,6})\\b").Select(m => m.Groups[1].Value.ToLowerInvariant())
            .Should().OnlyContain(c => c == "#000", "a térmica só imprime preto");
        corpo.Should().NotContain("var(--");
    }

    [Fact]
    public async Task JatoDeTintaSemFundoPreenchido()
    {
        var html = await Renderizar(Exemplo(), PedidoImpressoHtml.A4);
        var css = html[html.IndexOf("/* ===== Jato de tinta", StringComparison.Ordinal)..html.IndexOf("</style>", StringComparison.Ordinal)];

        Regex.Matches(css, "background[^;}]*").Select(m => m.Value.Trim())
            .Should().OnlyContain(b => b == "background: #fff", "cor só em linha, contorno e texto");
        Corpo(html).Should().NotContain("background");
    }

    [Fact]
    public async Task FontesDaApiEPaginaDoTamanhoDoPapel()
    {
        (await Renderizar(Exemplo(), PedidoImpressoHtml.Etiqueta10x15)).Should()
            .Contain("@page{size:100mm 150mm;margin:0}")
            .And.Contain("url(\"https://api.test/impressao/fontes/Lora.ttf\")");
        (await Renderizar(Exemplo(), PedidoImpressoHtml.Cupom58)).Should().Contain("@page{size:58mm auto;margin:0}");
        (await Renderizar(Exemplo(), PedidoImpressoHtml.A4)).Should().Contain("@page{size:A4;margin:0}");
    }

    [Fact]
    public async Task ModeloDesconhecidoLanca()
    {
        var act = () => Renderizar(Exemplo(), "bobina-80");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("+5511987654412", "(11) 98765-4412")]
    [InlineData("1133334444", "(11) 3333-4444")]
    [InlineData("ramal 12", "ramal 12")]
    public void FormataTelefone(string entrada, string esperado)
    {
        PedidoImpressoHtml.Telefone(entrada).Should().Be(esperado);
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

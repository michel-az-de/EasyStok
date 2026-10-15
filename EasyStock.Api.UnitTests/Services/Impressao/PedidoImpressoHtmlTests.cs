using System.Text.RegularExpressions;
using EasyStock.Api.Services.Impressao;
using EasyStock.Application.UseCases.Operacao.Impressao;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Infra.Notifications.Templating;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Api.UnitTests.Services.Impressao;

/// <summary>
/// #1632: pedido, agendamento e entrega saem no layout "bilhete da casa"
/// (docs/plan/atendimento-whatsapp/impressos/impressos-v4-proposta.html), em qualquer papel.
/// Snapshots em <c>Snapshots/</c>; regravar com <c>IMPRESSAO_ATUALIZAR_SNAPSHOT=1</c> e revisar o diff.
/// </summary>
public class PedidoImpressoHtmlTests
{
    private const string BaseUrl = "https://api.test";
    private const string Logo = "https://cdn.test/lojas/logo.png";

    [Theory]
    [MemberData(nameof(DocumentosEPapeis))]
    public async Task Barras_identificam_o_pedido_sem_instruir_camera_whatsapp(string documento, string papel)
    {
        var renderer = new PedidoImpressoHtml(new ScribanRenderer(NullLogger<ScribanRenderer>.Instance));
        var html = await renderer.RenderizarAsync(Exemplo(logo: null), documento, papel, BaseUrl, new ImpressoOpcoes(Codigo: "barras"));
        html.Should().Contain("class=\"barras\"").And.Contain("Código de barras A7F3C21B").And.NotContain("Aponte a câmera");
        if (Environment.GetEnvironmentVariable("IMPRESSAO_ARTEFATOS") is { Length: > 0 } pasta)
        {
            Directory.CreateDirectory(pasta);
            await File.WriteAllTextAsync(Path.Combine(pasta, documento + "-" + papel + "-barras.html"), html);
            if (documento == PedidoImpressoHtml.Entrega)
            {
                var longo = Exemplo(logo: null, clienteNome: "Maria Aparecida de Albuquerque e Vasconcelos",
                    itens: Enumerable.Range(1, 12).Select(n => new PedidoImpressoItemDto(2, "un",
                        $"Item {n}: pão de queijo recheado com queijo e ervas", "embalar separadamente", 24.90m, 49.80m)).ToArray());
                longo = longo with {
                    Cliente = longo.Cliente with { Endereco = "Rua das Laranjeiras, 214, apartamento 320, bloco Girassol, Vila Mariana, São Paulo, acesso pela portaria da avenida" },
                    Entrega = longo.Entrega! with { Responsavel = "Carlos Eduardo de Oliveira e Albuquerque" }
                };
                var htmlLongo = await renderer.RenderizarAsync(longo, documento, papel, BaseUrl);
                await File.WriteAllTextAsync(Path.Combine(pasta, "entrega-" + papel + "-longa.html"), htmlLongo);
            }
        }
    }

    public static TheoryData<string> Papeis => new(ImpressoHtml.Papeis);

    public static TheoryData<string, string> DocumentosEPapeis
    {
        get
        {
            var dados = new TheoryData<string, string>();
            foreach (var d in PedidoImpressoHtml.Documentos)
                foreach (var p in ImpressoHtml.Papeis)
                    dados.Add(d, p);
            return dados;
        }
    }

    internal static PedidoImpressoDto Exemplo(
        bool agendado = true, bool pago = false, string? clienteNome = "Mariana Souza", string? logo = Logo,
        string? whatsApp = "5511900000000", IReadOnlyList<PedidoImpressoItemDto>? itens = null, decimal recebido = 0m) => new(
        new PedidoImpressoCasaDto("Casa da Baba", "12345678000190", "https://casadababa.com.br/", whatsApp, logo),
        "A7F3C21B",
        agendado
            ? new PedidoImpressoPrazoDto(true, new DateTime(2026, 10, 2, 10, 0, 0), new DateTime(2026, 10, 2, 11, 0, 0), new DateTime(2026, 10, 2, 9, 30, 0))
            : new PedidoImpressoPrazoDto(false, new DateTime(2026, 10, 2, 15, 10, 0), null, new DateTime(2026, 10, 2, 14, 40, 0)),
        new PedidoImpressoClienteDto("0412AB", clienteNome, "+5511987654412", "Rua das Laranjeiras, 214, ap 32 - Vila Mariana, São Paulo"),
        new PedidoImpressoEntregaDto(TipoEntregador.Motoboy, "João"),
        itens ??
        [
            new PedidoImpressoItemDto(2, "un", "Pão de queijo recheado 500 g", null, 24.90m, 49.80m),
            new PedidoImpressoItemDto(1, "un", "Bolo de cenoura com cobertura de chocolate", "sem granulado", 42m, 42m),
            new PedidoImpressoItemDto(1.5m, "kg", "Massa fresca de espinafre", null, 17m, 25.50m),
            new PedidoImpressoItemDto(1, "un", "Torta de palmito média", null, 56m, 56m),
        ],
        "Deixar na portaria, bloco B",
        "Embalar os pães separados",
        new PedidoImpressoCobrancaDto(173.30m, pago, "pix", recebido),
        7,
        agendado ? new DateTime(2026, 9, 30, 14, 32, 0) : new DateTime(2026, 10, 2, 14, 32, 0),
        new DateTime(2026, 10, 1, 9, 12, 0),
        new DateTime(2026, 10, 2, 7, 40, 0));

    private static Task<string> Renderizar(
        PedidoImpressoDto dto, string documento, string modelo, ImpressoOpcoes? opcoes = null, IConfiguration? configuracao = null) =>
        new PedidoImpressoHtml(new ScribanRenderer(NullLogger<ScribanRenderer>.Instance), configuracao)
            .RenderizarAsync(dto, documento, modelo, BaseUrl, opcoes);

    [Theory]
    [InlineData(PedidoImpressoHtml.Pedido, ImpressoHtml.A5)]
    [InlineData(PedidoImpressoHtml.Pedido, ImpressoHtml.Cupom80)]
    [InlineData(PedidoImpressoHtml.Agendamento, ImpressoHtml.A5)]
    [InlineData(PedidoImpressoHtml.Agendamento, ImpressoHtml.Cupom58)]
    [InlineData(PedidoImpressoHtml.Entrega, ImpressoHtml.Etiqueta10x15)]
    [InlineData(PedidoImpressoHtml.Entrega, ImpressoHtml.A4)]
    public async Task Snapshot(string documento, string modelo)
    {
        var html = await Renderizar(Exemplo(), documento, modelo, new ImpressoOpcoes(Alergias: ["CASTANHA"]));
        SnapshotImpresso.Conferir($"{documento}.{modelo}.html", html);
    }

    [Theory]
    [MemberData(nameof(DocumentosEPapeis))]
    public async Task TodoDocumentoSaiEmTodoPapelComONumeroEOQr(string documento, string modelo)
    {
        var corpo = SnapshotImpresso.Corpo(await Renderizar(Exemplo(), documento, modelo));

        corpo.Should().ContainAll("Pedido A7F3C21B", "#A7F3C21B", "<svg class=\"qr\"", "Casa da Baba");
        corpo.Should().Contain($"d-{documento}");
    }

    [Theory]
    [MemberData(nameof(Papeis))]
    public async Task PedidoFalaComAClienteEDizOQueFaltaAcertar(string modelo)
    {
        var corpo = SnapshotImpresso.Corpo(await Renderizar(Exemplo(), PedidoImpressoHtml.Pedido, modelo));

        corpo.Should().ContainAll("Mariana, vamos à mesa?", "Seu pedido", "Pão de queijo recheado 500 g", "sem granulado", "49,80");
        corpo.Should().ContainAll("1,5 kg", "R$ 173,30", "A acertar na entrega · PIX.", "Faltou algo? Quer repetir?");
        corpo.Should().ContainAll("Feito com cuidado, acompanhado de perto.", "casadababa.com.br", "WhatsApp (11) 90000-0000", "impresso 02/10 07:40");
        corpo.Should().NotContainAny("Falta pagar", "CNPJ", "ID #");
    }

    [Fact]
    public async Task PedidoPagoAgradeceEmVezDeCobrar()
    {
        var corpo = SnapshotImpresso.Corpo(await Renderizar(Exemplo(pago: true), PedidoImpressoHtml.Pedido, ImpressoHtml.Cupom80));

        corpo.Should().Contain("Tudo pago no PIX. Obrigada!").And.NotContain("A acertar");
    }

    [Fact]
    public async Task AgendamentoTemOCombinadoNoTituloEAAlergiaAnotada()
    {
        var corpo = SnapshotImpresso.Corpo(await Renderizar(
            Exemplo(), PedidoImpressoHtml.Agendamento, ImpressoHtml.A5, new ImpressoOpcoes(Alergias: ["CASTANHA", "GLÚTEN"])));

        corpo.Should().ContainAll("Encomenda confirmada", "Mariana, combinado: sexta, 02/10, entre 10h e 11h.");
        corpo.Should().ContainAll("Anotado: alergia a castanha, glúten.", "pedido feito em 30/09 14:32", "Mudou o plano?");
    }

    [Fact]
    public async Task AgendamentoSemAlergiaNaoInventaAviso()
    {
        var corpo = SnapshotImpresso.Corpo(await Renderizar(Exemplo(), PedidoImpressoHtml.Agendamento, ImpressoHtml.Cupom80));

        corpo.Should().NotContain("Anotado");
    }

    [Theory]
    [InlineData(ImpressoHtml.Etiqueta10x15)]
    [InlineData(ImpressoHtml.Cupom80)]
    [InlineData(ImpressoHtml.Cupom58)]
    public async Task EntregaNaTermicaSaiEmDuasViasParaAGuilhotinaCortar(string modelo)
    {
        var html = await Renderizar(Exemplo(), PedidoImpressoHtml.Entrega, modelo);
        var corpo = SnapshotImpresso.Corpo(html);

        Regex.Matches(corpo, "<article ").Should().HaveCount(2);
        html.Should().Contain(".im.v1{break-after:page}");
        var viaDoEntregador = corpo[..corpo.IndexOf("</article>", StringComparison.Ordinal)];
        var viaDoPacote = corpo[corpo.IndexOf("</article>", StringComparison.Ordinal)..];

        viaDoEntregador.Should().ContainAll(" v1\"", "via do entregador", "Rua das Laranjeiras, 214", "(11) 98765-4412", "Deixar na portaria, bloco B");
        viaDoPacote.Should().ContainAll(" v2\"", "Para Mariana", "Neste pedido", "Chegou diferente?", "Da nossa casa para a sua mesa.");
        viaDoPacote.Should().NotContainAny("98765-4412", "RECEBER", "R$", "Rua das Laranjeiras");
    }

    [Theory]
    [InlineData(ImpressoHtml.A4)]
    [InlineData(ImpressoHtml.A5)]
    public async Task EntregaNoJatoDeTintaEUmaFolhaComLinhaDeDobra(string modelo)
    {
        var corpo = SnapshotImpresso.Corpo(await Renderizar(Exemplo(), PedidoImpressoHtml.Entrega, modelo));

        Regex.Matches(corpo, "<article ").Should().HaveCount(1);
        corpo.Should().ContainAll("dobre ou corte aqui", "via do entregador", "Para Mariana");
    }

    [Fact]
    public async Task EntregaACobrarGritaOValorEAPagaDizQueNaoRecebeNada()
    {
        var aCobrar = SnapshotImpresso.Corpo(await Renderizar(Exemplo(), PedidoImpressoHtml.Entrega, ImpressoHtml.Cupom80));
        var paga = SnapshotImpresso.Corpo(await Renderizar(Exemplo(pago: true), PedidoImpressoHtml.Entrega, ImpressoHtml.Cupom80));

        aCobrar.Should().ContainAll("<div class=\"alerta\">RECEBER R$ 173,30<small>PIX</small></div>", "Motoboy · João").And.NotContain("Já está pago");
        paga.Should().Contain("Nada. Já está pago.").And.NotContain("RECEBER");
    }

    [Fact]
    public async Task ComSinalOEntregadorCobraSoOSaldo()
    {
        var dto = Exemplo(recebido: 50m);

        var entrega = SnapshotImpresso.Corpo(await Renderizar(dto, PedidoImpressoHtml.Entrega, ImpressoHtml.Cupom80));
        var pedido = SnapshotImpresso.Corpo(await Renderizar(dto, PedidoImpressoHtml.Pedido, ImpressoHtml.Cupom80));

        entrega.Should().Contain("RECEBER R$ 123,30<small>PIX</small>").And.NotContain("RECEBER R$ 173,30");
        pedido.Should().ContainAll("R$ 173,30", "Falta R$ 123,30, a acertar na entrega · PIX.");
    }

    [Theory]
    [MemberData(nameof(DocumentosEPapeis))]
    public async Task ClienteSemNomeNaoDeixaFraseQuebrada(string documento, string modelo)
    {
        var corpo = SnapshotImpresso.Corpo(await Renderizar(Exemplo(clienteNome: null), documento, modelo));

        corpo.Should().NotContainAny("<h1>, ", "<h1>Para </h1>", ">Para <");
        corpo.Should().ContainAny("<h1>Vamos à mesa?</h1>", "<h1>Combinado: ", "<h1>Seu pedido chegou</h1>");
    }

    [Theory]
    [InlineData(ImpressoHtml.A4, false, true)]
    [InlineData(ImpressoHtml.A5, false, true)]
    [InlineData(ImpressoHtml.Cupom80, false, false)]
    [InlineData(ImpressoHtml.Cupom58, true, true)]
    [InlineData(ImpressoHtml.Etiqueta10x15, true, true)]
    public async Task LogoDaLojaSaiNoJatoDeTintaENaTermicaSoQuandoPedido(string modelo, bool logoNaTermica, bool esperaLogo)
    {
        var corpo = SnapshotImpresso.Corpo(await Renderizar(
            Exemplo(), PedidoImpressoHtml.Pedido, modelo, new ImpressoOpcoes(LogoNaTermica: logoNaTermica)));

        if (esperaLogo)
            corpo.Should().Contain($"<img class=\"logo\" src=\"{Logo}\" alt=\"Casa da Baba\">").And.NotContain("class=\"wm\"");
        else
            corpo.Should().Contain("<div class=\"wm\">Casa da Baba</div>").And.NotContain("<img");
    }

    [Fact]
    public async Task SemLogoCadastradoSaiONomeDaCasaEmTexto()
    {
        var corpo = SnapshotImpresso.Corpo(await Renderizar(Exemplo(logo: null), PedidoImpressoHtml.Pedido, ImpressoHtml.A4));

        corpo.Should().Contain("<div class=\"wm\">Casa da Baba</div>").And.NotContain("<img");
    }

    [Fact]
    public async Task LogoDeCaminhoRelativoViraEnderecoDaApi()
    {
        var corpo = SnapshotImpresso.Corpo(await Renderizar(Exemplo(logo: "/uploads/lojas/logo.png"), PedidoImpressoHtml.Pedido, ImpressoHtml.A4));

        corpo.Should().Contain("<img class=\"logo\" src=\"https://api.test/uploads/lojas/logo.png\"");
    }

    [Fact]
    public async Task SemWhatsAppDaCasaNaoHaQrNemConvite()
    {
        var corpo = SnapshotImpresso.Corpo(await Renderizar(Exemplo(whatsApp: null), PedidoImpressoHtml.Pedido, ImpressoHtml.Cupom80));

        corpo.Should().NotContain("<svg class=\"qr\"").And.NotContain("Faltou algo?");
    }

    [Fact]
    public async Task AssinaturaVemDaConfiguracaoESemElaAssinaACasa()
    {
        var comDona = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [PedidoImpressoHtml.ChaveAssinatura] = "Thati" })
            .Build();

        (await Renderizar(Exemplo(), PedidoImpressoHtml.Pedido, ImpressoHtml.Cupom80, configuracao: comDona))
            .Should().Contain("<p class=\"ass\">Thati · Casa da Baba</p>");
        (await Renderizar(Exemplo(), PedidoImpressoHtml.Pedido, ImpressoHtml.Cupom80))
            .Should().Contain("<p class=\"ass\">Casa da Baba</p>");
    }

    [Fact]
    public async Task ImediatoDizHojeEPorVoltaDe()
    {
        var corpo = SnapshotImpresso.Corpo(await Renderizar(Exemplo(agendado: false), PedidoImpressoHtml.Pedido, ImpressoHtml.A4));

        corpo.Should().ContainAll("hoje · ~15:10", "hoje, por volta das 15:10");
    }

    [Theory]
    [MemberData(nameof(DocumentosEPapeis))]
    public async Task EscapaTextoDoCliente(string documento, string modelo)
    {
        var dto = Exemplo(clienteNome: "<script>alert(1)</script>", itens: [new PedidoImpressoItemDto(1, "un", "<b>Bolo</b>", "<i>obs</i>", 10m, 10m)]);

        var corpo = SnapshotImpresso.Corpo(await Renderizar(dto, documento, modelo));

        corpo.Should().NotContain("<script>").And.NotContain("<b>Bolo").And.NotContain("<i>obs");
        corpo.Should().Contain("&lt;b&gt;Bolo&lt;/b&gt;");
    }

    [Theory]
    [InlineData(PedidoImpressoHtml.Pedido)]
    [InlineData(PedidoImpressoHtml.Agendamento)]
    [InlineData(PedidoImpressoHtml.Entrega)]
    public async Task TermicaSoPretoSemCorNoCorpo(string documento)
    {
        foreach (var modelo in new[] { ImpressoHtml.Etiqueta10x15, ImpressoHtml.Cupom80, ImpressoHtml.Cupom58 })
        {
            var html = await Renderizar(Exemplo(), documento, modelo);
            SnapshotImpresso.CoresDoCorpo(html).Should().OnlyContain(c => c == "#000", "a térmica só imprime preto");
            SnapshotImpresso.Corpo(html).Should().Contain(" termica ").And.NotContain(" tinta ");
        }
    }

    [Fact]
    public async Task TermicaNoCssEPretoPuroEJatoDeTintaNaoTemFundoEscuro()
    {
        var html = await Renderizar(Exemplo(), PedidoImpressoHtml.Pedido, ImpressoHtml.A4);

        html.Should().Contain(".im.termica{--tinta:#000;--apoio:#000;--acento:#000;--marca:#000;--linha:#000;");
        html.Should().Contain(".im.tinta .alerta{background:#fff;");
        html.Should().NotContain("#FBF5EC", "papel branco: sem creme na impressão");
    }

    [Fact]
    public async Task FontesDaApiEPaginaDoTamanhoDoPapel()
    {
        (await Renderizar(Exemplo(), PedidoImpressoHtml.Pedido, ImpressoHtml.Etiqueta10x15)).Should()
            .Contain("@page{size:100mm 150mm;margin:0}")
            .And.Contain("url(\"https://api.test/impressao/fontes/Lora.ttf\")");
        // "80mm auto" é sintaxe inválida e o navegador cai no A4 (#1756): a bobina sai com a largura no .im e o
        // Console mede o comprimento antes de imprimir.
        foreach (var bobina in new[] { ImpressoHtml.Cupom80, ImpressoHtml.Cupom58 })
            (await Renderizar(Exemplo(), PedidoImpressoHtml.Pedido, bobina)).Should().Contain("@page{size:auto;margin:0}")
                .And.NotContain("mm auto");
        (await Renderizar(Exemplo(), PedidoImpressoHtml.Pedido, ImpressoHtml.A4)).Should().Contain("@page{size:A4;margin:0}");
        (await Renderizar(Exemplo(), PedidoImpressoHtml.Pedido, ImpressoHtml.A5)).Should().Contain("@page{size:A5;margin:0}");
    }

    [Fact]
    public async Task ModeloOuDocumentoDesconhecidoLanca()
    {
        var modelo = () => Renderizar(Exemplo(), PedidoImpressoHtml.Pedido, "carta");
        var documento = () => Renderizar(Exemplo(), "recibo", ImpressoHtml.A4);

        await modelo.Should().ThrowAsync<ArgumentException>();
        await documento.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("5511900000000", "https://wa.me/5511900000000?text=Pedido%20A7F3C21B")]
    [InlineData("(11) 90000-0000", "https://wa.me/5511900000000?text=Pedido%20A7F3C21B")]
    [InlineData("ramal 12", null)]
    [InlineData(null, null)]
    public void LinkDoWhatsAppLevaOPedidoNoTexto(string? whatsApp, string? esperado)
    {
        ImpressoHtml.LinkWhatsApp(whatsApp, "A7F3C21B").Should().Be(esperado);
    }

    [Theory]
    [InlineData("+5511987654412", "(11) 98765-4412")]
    [InlineData("1133334444", "(11) 3333-4444")]
    [InlineData("ramal 12", "ramal 12")]
    public void FormataTelefone(string entrada, string esperado)
    {
        PedidoImpressoHtml.Telefone(entrada).Should().Be(esperado);
    }
}

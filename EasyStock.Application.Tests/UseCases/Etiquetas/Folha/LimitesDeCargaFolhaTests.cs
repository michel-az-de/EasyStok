using System.Text.Json;
using System.Text.Json.Nodes;
using EasyStock.Application.Ports.Output.Pdf;
using EasyStock.Application.UseCases.Etiquetas.Folha;
using static EasyStock.Application.Tests.UseCases.Etiquetas.Folha.LayoutsDeTeste;

namespace EasyStock.Application.Tests.UseCases.Etiquetas.Folha;

/// <summary>
/// #2200, banca de DoS: grade sem piso, layout gigante, CPU sem teto, estouro de decimal e valores ecoados sem limite.
/// Tudo vira 400 antes de gerar um QR ou uma barra.
/// </summary>
public class LimitesDeCargaFolhaTests
{
    private readonly IQrCodeEncoder _qr = Substitute.For<IQrCodeEncoder>();
    private readonly GerarFolhaEtiquetasPdfUseCase _sut;

    public LimitesDeCargaFolhaTests()
    {
        _qr.Matriz(Arg.Any<string>()).Returns(_ => new bool[29, 29]);
        _sut = ComVagas(new VagasDeFolhaPdf());
    }

    private static GerarFolhaEtiquetasPdfCommand Pimaco(params EtiquetaNaFolha[] etiquetas) =>
        new(new FolhaSpec("pimaco-a4256", null, null, null), Guias: false, 0, etiquetas);

    private static GerarFolhaEtiquetasPdfCommand QuantasCabem(decimal margem, decimal espaco, params EtiquetaNaFolha[] etiquetas) =>
        new(new FolhaSpec("quantas-cabem", null, margem, espaco), Guias: false, 0, etiquetas);

    private static JsonObject Divisor(decimal x, decimal y, decimal w, decimal h) => new()
    {
        ["id"] = "div", ["type"] = "divider", ["x_mm"] = x, ["y_mm"] = y, ["w_mm"] = w, ["h_mm"] = h,
    };

    private static JsonObject QrCom(string conteudo) => Codigo("qr", "qr", conteudo, 45m, 3m, 15.5m, 15.5m, 0.5m);

    private static JsonObject EtiquetaPesadaDeTexto() => Layout(63.5m, 38.1m, "horizontal",
        Enumerable.Range(0, 40).Select(i => Texto($"dado{i}", new string('a', 500), 1m, 1m + i % 10 * 3.5m, 60m, 3.2m, 7.5m)).ToArray());

    private string Recusa(GerarFolhaEtiquetasPdfCommand command)
    {
        var planejar = () => _sut.Planejar(command);
        var ex = planejar.Should().Throw<UseCaseValidationException>().Which;
        ex.Code.Should().Be(GerarFolhaEtiquetasPdfUseCase.CodigoErro);
        return ex.Message;
    }

    // ---------- 1. "quantas-cabem" sem piso ----------

    [Fact]
    public void QuantasCabem_EtiquetaDe05mm_RecusaAntesDeMontarAGrade() =>
        Recusa(QuantasCabem(0m, 0m, Etiqueta(Layout(0.5m, 0.5m, "horizontal", Divisor(0m, 0m, 0.5m, 0.5m)))))
            .Should().Contain("10 mm");

    [Fact]
    public void QuantasCabem_SoAAlturaAbaixoDe10mm_Recusa() =>
        Recusa(QuantasCabem(5m, 3m, Etiqueta(Layout(50m, 9.9m, "horizontal", Divisor(0m, 0m, 50m, 9.9m)))))
            .Should().Contain("10 mm");

    [Fact]
    public void QuantasCabem_MaisDe500PosicoesPorFolha_Recusa() =>
        Recusa(QuantasCabem(0m, 0m, Etiqueta(Layout(10m, 10m, "horizontal", Divisor(0m, 0m, 10m, 10m)))))
            .Should().Contain("500");

    [Fact]
    public void QuantasCabem_10mmComMargemPadrao_AbaixoDoTeto_Aceita()
    {
        var plano = _sut.Planejar(QuantasCabem(5m, 3m, Etiqueta(Layout(10m, 10m, "horizontal", Divisor(0m, 0m, 10m, 10m)))));

        plano.Paginas[0].Celulas.Should().HaveCount(15 * 22);
    }

    [Fact]
    public void GradeFolha_QuantasCabem_AcimaDoTetoDeCelulas_DevolveNulo() =>
        GradeFolha.QuantasCabem(0.5m, 0.5m, 0m, 0m).Should().BeNull();

    // ---------- 2. Layout gigante desserializado antes do teto de 40 ----------

    [Fact]
    public void Layout_ComMaisDe40Elementos_RecusaSemDesserializarOsElementos()
    {
        var elementos = new[] { new JsonObject { ["x_mm"] = "abc" } }.Concat(Enumerable.Range(0, 40).Select(_ => new JsonObject())).ToArray();

        var mensagem = Recusa(Pimaco(Etiqueta(Layout(63.5m, 38.1m, "horizontal", elementos))));

        mensagem.Should().Contain("40 elementos").And.NotContain("LayoutJson");
    }

    [Fact]
    public void LayoutEmTexto_ComMaisDe40Elementos_RecusaSemDesserializarOsElementos()
    {
        var elementos = new[] { new JsonObject { ["x_mm"] = "abc" } }.Concat(Enumerable.Range(0, 40).Select(_ => new JsonObject())).ToArray();
        var emTexto = new EtiquetaNaFolha(JsonSerializer.SerializeToElement(Layout(63.5m, 38.1m, "horizontal", elementos).ToJsonString()), null);

        var mensagem = Recusa(Pimaco(emTexto));

        mensagem.Should().Contain("40 elementos").And.NotContain("LayoutJson");
    }

    // ---------- 3. CPU sem teto ----------

    [Fact]
    public void Pimaco_LayoutQueNaoCabe_RecusaSemGerarQr()
    {
        Recusa(Pimaco(Etiqueta(Layout(200m, 130m, "horizontal", Qr(10m, 10m, 40m, 0m))))).Should().Contain("não cabe");

        _qr.DidNotReceiveWithAnyArgs().Matriz(default!);
    }

    [Fact]
    public void QuantasCabem_MaisDe50Paginas_RecusaSemGerarQr()
    {
        Recusa(QuantasCabem(5m, 3m, Etiqueta(Layout(150m, 250m, "vertical", Qr(10m, 10m, 40m, 0m)), copias: 51)))
            .Should().Contain("50 páginas");

        _qr.DidNotReceiveWithAnyArgs().Matriz(default!);
    }

    [Fact]
    public void CodigosIguais_EmEtiquetasDiferentes_GeramUmaMatrizSoNoPedido()
    {
        var etiquetas = Enumerable.Range(0, 3).Select(_ => Etiqueta(PimacoCompleta(), copias: 2)).ToArray();

        _sut.Planejar(Pimaco(etiquetas));

        _qr.Received(1).Matriz(ConteudoQr);
    }

    [Fact]
    public void Pedido_ComMaisDe200CodigosDistintos_RecusaSemGerarQr()
    {
        var etiquetas = Enumerable.Range(0, 201)
            .Select(i => Etiqueta(Layout(63.5m, 38.1m, "horizontal", QrCom($"LOT-{i}")))).ToArray();

        Recusa(Pimaco(etiquetas)).Should().Contain("200 códigos");

        _qr.DidNotReceiveWithAnyArgs().Matriz(default!);
    }

    [Fact]
    public void Pedido_Com200CodigosDistintos_Aceita()
    {
        var etiquetas = Enumerable.Range(0, 200)
            .Select(i => Etiqueta(Layout(63.5m, 38.1m, "horizontal", QrCom($"LOT-{i}")))).ToArray();

        _sut.Planejar(Pimaco(etiquetas));

        _qr.ReceivedWithAnyArgs(200).Matriz(default!);
    }

    [Fact]
    public void Pedido_ComMaisDe200MilCaracteresDeTexto_Recusa() =>
        Recusa(Pimaco(Etiqueta(EtiquetaPesadaDeTexto(), copias: 11))).Should().Contain("texto");

    [Fact]
    public void Pedido_Com200MilCaracteresDeTexto_Aceita()
    {
        var plano = _sut.Planejar(Pimaco(Etiqueta(EtiquetaPesadaDeTexto(), copias: 10)));

        plano.Paginas.Should().NotBeEmpty();
    }

    // ---------- 4. Estouro de decimal vira 400 ----------

    [Fact]
    public void Overflow_LarguraDaEtiquetaNoMaximoDoDecimal_Recusa400() =>
        Recusa(QuantasCabem(5m, 3m, Etiqueta(Layout(decimal.MaxValue, 10m, "horizontal", Divisor(0m, 0m, 10m, 10m)))))
            .Should().Contain("1000 mm");

    [Fact]
    public void Overflow_ElementoComXMaisWNoMaximoDoDecimal_Recusa400() =>
        Recusa(Pimaco(Etiqueta(Layout(50m, 30m, "horizontal", Divisor(decimal.MaxValue, 0m, decimal.MaxValue, 1m)))))
            .Should().Contain("1000 mm");

    [Fact]
    public void Overflow_QuietZoneNoMaximoDoDecimal_Recusa400() =>
        Recusa(Pimaco(Etiqueta(Layout(63.5m, 38.1m, "horizontal", Qr(45m, 3m, 15.5m, decimal.MaxValue)))))
            .Should().Contain("1000 mm");

    [Fact]
    public void Etiqueta_ComMaisDe1000mm_Recusa() =>
        Recusa(Pimaco(Etiqueta(Layout(1001m, 10m, "horizontal", Divisor(0m, 0m, 10m, 10m))))).Should().Contain("1000 mm");

    [Fact]
    public void Elemento_ComMaisDe1000mm_Recusa() =>
        Recusa(Pimaco(Etiqueta(Layout(50m, 30m, "horizontal", Divisor(0m, 0m, 1001m, 1m))))).Should().Contain("1000 mm");

    // ---------- 5. Valores ecoados sem limite ----------

    [Fact]
    public void ValoresEcoadosNosErros_SaemTruncados()
    {
        var longo = new string('A', 5000);
        var layout = Layout(63.5m, 38.1m, "horizontal",
            new JsonObject { ["id"] = longo, ["type"] = longo, ["x_mm"] = 1m, ["y_mm"] = 20m, ["w_mm"] = 5m, ["h_mm"] = 5m },
            new JsonObject { ["id"] = "c", ["type"] = "code", ["format"] = longo, ["content"] = "x", ["x_mm"] = 10m, ["y_mm"] = 20m, ["w_mm"] = 5m, ["h_mm"] = 5m },
            Qr(),
            Texto(longo, "x", 44m, 3m, 5m, 3.5m, 7.5m));

        var mensagem = Recusa(Pimaco(Etiqueta(layout)));

        mensagem.Should().Contain(new string('A', 40)).And.NotContain(new string('A', 100));
        mensagem.Length.Should().BeLessThan(1000);
    }

    // ---------- 6. Rebanca: chave "elements" repetida burlava o teto de 40 ----------

    private static readonly string ElementosGrandes =
        "[" + string.Join(",", new[] { """{"x_mm":"abc"}""" }.Concat(Enumerable.Repeat("{}", 40))) + "]";

    private static readonly string ElementosPequenos = new JsonArray(Divisor(0m, 0m, 10m, 1m)).ToJsonString();

    private static EtiquetaNaFolha ComElementsDuasVezes(string primeiro, string segundo)
    {
        var tamanho = new JsonObject { ["preset"] = "custom", ["w_mm"] = 63.5m, ["h_mm"] = 38.1m, ["orientation"] = "horizontal" }.ToJsonString();
        using var documento = JsonDocument.Parse($$"""{"v":1,"size":{{tamanho}},"elements":{{primeiro}},"elements":{{segundo}}}""");
        return new EtiquetaNaFolha(documento.RootElement.Clone(), null);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Layout_ComChaveElementsRepetida_RecusaSemDesserializarOsElementos(bool grandePrimeiro)
    {
        var etiqueta = grandePrimeiro
            ? ComElementsDuasVezes(ElementosGrandes, ElementosPequenos)
            : ComElementsDuasVezes(ElementosPequenos, ElementosGrandes);

        Recusa(Pimaco(etiqueta)).Should().NotContain("LayoutJson", "o elemento inválido do array grande não pode ser desserializado");
    }

    [Fact]
    public void Layout_ComChaveElementsRepetida_MesmoPequena_Recusa() =>
        Recusa(Pimaco(ComElementsDuasVezes(ElementosPequenos, ElementosPequenos))).Should().Contain("elements aparece mais de uma vez");

    // ---------- 7. Rebanca: cada glifo fora da Lato custa uma busca de fonte no render ----------

    private static string Cjk(int inicio, int quantos) =>
        string.Concat(Enumerable.Range(0x4E00 + inicio, quantos).Select(c => (char)c));

    private static EtiquetaNaFolha EtiquetaComTexto(string texto, int? copias = null) =>
        Etiqueta(Layout(63.5m, 38.1m, "horizontal", Texto("nome", texto, 1m, 1m, 60m, 5m, 10m)), copias);

    [Fact]
    public void Pedido_ComMaisDe64GlifosDistintosForaDoLatino_SomadosEntreEtiquetas_Recusa() =>
        Recusa(Pimaco(EtiquetaComTexto(Cjk(0, 40)), EtiquetaComTexto(Cjk(40, 25)))).Should().Contain("64");

    [Fact]
    public void Pedido_Com64GlifosForaDoLatino_RepetidosECopiados_Aceita() =>
        _sut.Planejar(Pimaco(EtiquetaComTexto(Cjk(0, 64) + Cjk(0, 64), copias: 10))).Paginas.Should().NotBeEmpty();

    [Fact]
    public void Texto_ComTodoOLatin1EOLatinExtendedA_NaoContaComoGlifoForaDoLatino()
    {
        var latino = string.Concat(Enumerable.Range(0xA0, 0x180 - 0xA0).Select(c => (char)c)) + "–—‘’“”•…€™";

        _sut.Planejar(Pimaco(EtiquetaComTexto(latino))).Paginas.Should().NotBeEmpty();
    }

    // ---------- 8. Rebanca: os QR rodavam fora do teto de renders simultâneos ----------

    private GerarFolhaEtiquetasPdfUseCase ComVagas(VagasDeFolhaPdf vagas) =>
        new(Substitute.For<IFolhaEtiquetasPdfRenderer>(), _qr, vagas);

    [Fact]
    public async Task ExecuteAsync_SemVaga_RecusaComOcupadoSemGerarQr()
    {
        var vagas = new VagasDeFolhaPdf(1, TimeSpan.FromMilliseconds(50));
        using var ocupada = await vagas.OcuparAsync();

        var gerar = () => ComVagas(vagas).ExecuteAsync(Pimaco(Etiqueta(Layout(63.5m, 38.1m, "horizontal", QrCom("LOT-1")))));

        await gerar.Should().ThrowAsync<FolhaPdfOcupadaException>();
        _qr.DidNotReceiveWithAnyArgs().Matriz(default!);
    }

    [Fact]
    public async Task ExecuteAsync_DevolveAVaga_DepoisDoPdfEDepoisDaRecusa()
    {
        var vagas = new VagasDeFolhaPdf(1, TimeSpan.FromMilliseconds(50));
        var sut = ComVagas(vagas);

        await sut.ExecuteAsync(Pimaco(Etiqueta(Layout(63.5m, 38.1m, "horizontal", QrCom("LOT-1")))));
        var recusa = () => sut.ExecuteAsync(Pimaco());
        await recusa.Should().ThrowAsync<UseCaseValidationException>();

        var ocupar = () => vagas.OcuparAsync();
        await ocupar.Should().NotThrowAsync("a vaga volta no fim do pedido, com PDF ou com 400");
    }

    [Fact]
    public async Task OcuparAsync_ComTodasAsVagasOcupadas_CancelaComOToken()
    {
        var vagas = new VagasDeFolhaPdf(1, TimeSpan.FromMinutes(1));
        using var ocupada = await vagas.OcuparAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var ocupar = () => vagas.OcuparAsync(cts.Token);

        await ocupar.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Vaga_DevolvidaDuasVezes_ContaUmaSo()
    {
        var vagas = new VagasDeFolhaPdf(1, TimeSpan.FromMilliseconds(50));
        var vaga = await vagas.OcuparAsync();
        vaga.Dispose();
        vaga.Dispose();

        using var primeira = await vagas.OcuparAsync();
        var segunda = () => vagas.OcuparAsync();

        await segunda.Should().ThrowAsync<FolhaPdfOcupadaException>();
    }
}

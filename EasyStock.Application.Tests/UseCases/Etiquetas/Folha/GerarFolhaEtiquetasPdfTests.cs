using System.Text.Json;
using System.Text.Json.Nodes;
using EasyStock.Application.Ports.Output.Pdf;
using EasyStock.Application.UseCases.Etiquetas.Folha;
using static EasyStock.Application.Tests.UseCases.Etiquetas.Folha.LayoutsDeTeste;

namespace EasyStock.Application.Tests.UseCases.Etiquetas.Folha;

public class GerarFolhaEtiquetasPdfTests
{
    private readonly IFolhaEtiquetasPdfRenderer _renderer = Substitute.For<IFolhaEtiquetasPdfRenderer>();
    private readonly GerarFolhaEtiquetasPdfUseCase _sut;

    public GerarFolhaEtiquetasPdfTests() => _sut = new GerarFolhaEtiquetasPdfUseCase(_renderer, new QrFalso(), new VagasDeFolhaPdf());

    private static GerarFolhaEtiquetasPdfCommand Pimaco(int pular = 0, params EtiquetaNaFolha[] etiquetas) =>
        new(new FolhaSpec("pimaco-a4256", null, null, null), Guias: true, pular, etiquetas);

    private static GerarFolhaEtiquetasPdfCommand QuantasCabem(params EtiquetaNaFolha[] etiquetas) =>
        new(new FolhaSpec("quantas-cabem", null, null, null), Guias: false, 0, etiquetas);

    private string Recusa(GerarFolhaEtiquetasPdfCommand command)
    {
        var planejar = () => _sut.Planejar(command);
        var ex = planejar.Should().Throw<UseCaseValidationException>().Which;
        ex.Code.Should().Be(GerarFolhaEtiquetasPdfUseCase.CodigoErro);
        return ex.Message;
    }

    // ---------- Geometria e posicionamento ----------

    [Fact]
    public void Pimaco_UmaEtiqueta_OcupaAPrimeiraPosicaoSemGirar()
    {
        var plano = _sut.Planejar(Pimaco(0, Etiqueta(PimacoCompleta())));

        plano.PaginaWMm.Should().Be(210m);
        plano.PaginaHMm.Should().Be(297m);
        plano.Guias.Should().BeTrue();
        var celulas = plano.Paginas.Should().ContainSingle().Which.Celulas;
        celulas.Should().HaveCount(21);
        celulas[0].Etiqueta.Should().NotBeNull();
        celulas[0].Girar.Should().BeFalse();
        (celulas[0].EtiquetaXMm, celulas[0].EtiquetaYMm).Should().Be((7.21m, 15.15m));
        celulas.Skip(1).Should().OnlyContain(c => c.Etiqueta == null);
    }

    [Fact]
    public void Pimaco_ElementosViramPlanoNaMesmaPosicao()
    {
        var etiqueta = _sut.Planejar(Pimaco(0, Etiqueta(PimacoCompleta()))).Paginas[0].Celulas[0].Etiqueta!;

        var qr = etiqueta.Elementos.OfType<QrPlano>().Should().ContainSingle().Which;
        (qr.XMm, qr.YMm, qr.LadoMm).Should().Be((45.5m, 3.5m, 14.5m), "a zona de silêncio declarada sai da caixa");
        qr.Matriz.GetLength(0).Should().Be(29);
        var barras = etiqueta.Elementos.OfType<BarrasPlano>().Should().ContainSingle().Which;
        (barras.XMm, barras.WMm, barras.HMm).Should().Be((6.625m, 30.75m, 8m));
        barras.Modulos.Should().Be(Code128.Codificar(ConteudoBarras).Modulos);
        var nome = etiqueta.Elementos.OfType<TextoPlano>().Single(t => t.Texto.StartsWith("Talharim"));
        (nome.TamanhoPt, nome.Peso, nome.Alinhamento).Should().Be((10m, 700, AlinhamentoTexto.Esquerda));
        etiqueta.Elementos.OfType<TextoPlano>().Single(t => t.Texto == ConteudoBarras)
            .Alinhamento.Should().Be(AlinhamentoTexto.Centro);
    }

    [Fact]
    public void Pimaco_LayoutVertical_GiraNaPosicao()
    {
        var plano = _sut.Planejar(Pimaco(0, Etiqueta(Simples(38.1m, 63.5m, "vertical"))));

        var celula = plano.Paginas[0].Celulas[0];
        celula.Girar.Should().BeTrue();
        (celula.EtiquetaXMm, celula.EtiquetaYMm).Should().Be((7.21m, 15.15m));
    }

    [Fact]
    public void Pimaco_LayoutMenor_FicaCentradoNaPosicao()
    {
        var celula = _sut.Planejar(Pimaco(0, Etiqueta(Simples(50m, 30m, "horizontal")))).Paginas[0].Celulas[0];

        celula.Girar.Should().BeFalse();
        (celula.EtiquetaXMm, celula.EtiquetaYMm).Should().Be((7.21m + 6.75m, 15.15m + 4.05m));
    }

    [Fact]
    public void Pular_E_Copias_QuebramPagina()
    {
        var plano = _sut.Planejar(Pimaco(20, Etiqueta(PimacoCompleta(), copias: 2)));

        plano.Paginas.Should().HaveCount(2);
        plano.Paginas[0].Celulas.Take(20).Should().OnlyContain(c => c.Etiqueta == null, "as 20 primeiras já foram usadas");
        plano.Paginas[0].Celulas[20].Etiqueta.Should().NotBeNull();
        plano.Paginas[1].Celulas[0].Etiqueta.Should().NotBeNull();
        plano.Paginas[1].Celulas.Skip(1).Should().OnlyContain(c => c.Etiqueta == null);
    }

    [Fact]
    public void QuantasCabem_52x75_Planeja3x3()
    {
        var plano = _sut.Planejar(QuantasCabem(Etiqueta(Simples(52m, 75m, "vertical"), copias: 9)));

        var celulas = plano.Paginas.Should().ContainSingle().Which.Celulas;
        celulas.Should().HaveCount(9).And.OnlyContain(c => c.Etiqueta != null && !c.Girar);
        (celulas[0].EtiquetaXMm, celulas[0].EtiquetaYMm).Should().Be((24m, 33m));
    }

    [Fact]
    public void LayoutComoTextoJson_TambemServe()
    {
        var texto = JsonSerializer.SerializeToElement(PimacoCompleta().ToJsonString());

        var plano = _sut.Planejar(Pimaco(0, new EtiquetaNaFolha(texto, 1)));

        plano.Paginas[0].Celulas[0].Etiqueta.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_DesenhaOPlano()
    {
        _renderer.RenderAsync(Arg.Any<PlanoFolhaEtiquetas>(), Arg.Any<CancellationToken>()).Returns([1, 2, 3]);

        var pdf = await _sut.ExecuteAsync(Pimaco(0, Etiqueta(PimacoCompleta())));

        pdf.Should().Equal(1, 2, 3);
        await _renderer.Received(1).RenderAsync(Arg.Is<PlanoFolhaEtiquetas>(p => p.Paginas.Count == 1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_Invalido_NaoDesenha()
    {
        var executar = () => _sut.ExecuteAsync(Pimaco(0, Etiqueta(PimacoCompleta(nomePt: 9m))));

        await executar.Should().ThrowAsync<UseCaseValidationException>();
        await _renderer.DidNotReceiveWithAnyArgs().RenderAsync(default!, default);
    }

    // ---------- Pisos de leitura ----------

    [Fact]
    public void NomeAbaixoDe10pt_Recusa() =>
        Recusa(Pimaco(0, Etiqueta(PimacoCompleta(nomePt: 9.5m)))).Should().Contain("'nome'").And.Contain("10 pt");

    [Fact]
    public void ValidadeAbaixoDe9pt_Recusa() =>
        Recusa(Pimaco(0, Etiqueta(PimacoCompleta(valPt: 8.5m)))).Should().Contain("'val'").And.Contain("9 pt");

    [Fact]
    public void DadoAbaixoDe7e5pt_Recusa() =>
        Recusa(Pimaco(0, Etiqueta(PimacoCompleta(lotePt: 7m)))).Should().Contain("'lote'").And.Contain("7,5 pt");

    [Fact]
    public void FonteMaiorQueACaixa_Recusa() =>
        Recusa(Pimaco(0, Etiqueta(PimacoCompleta(lotePt: 12m)))).Should().Contain("maior que a altura");

    [Fact]
    public void VariavelNaoResolvida_Recusa() =>
        Recusa(Pimaco(0, Etiqueta(PimacoCompleta(nome: "{produto.nome}")))).Should().Contain("{produto.nome}");

    [Fact]
    public void Ean13_RecusaPedindoGtin() =>
        Recusa(Pimaco(0, Etiqueta(PimacoCompleta(barras: Codigo("barras", "barcode-ean13", "7891234567895", 4.125m, 25.16m, 35.75m, 8m, 2.5m)))))
            .Should().Contain("GTIN");

    [Fact]
    public void QrComModuloAbaixoDe0375mm_Recusa() =>
        Recusa(Pimaco(0, Etiqueta(PimacoCompleta(qr: Qr(x: 50m, y: 5m, lado: 10m, quiet: 0m))))).Should().Contain("módulo");

    [Fact]
    public void QrSemZonaDeSilencioDe4Modulos_Recusa() =>
        Recusa(Pimaco(0, Etiqueta(PimacoCompleta(qr: Qr(x: 47.5m, y: 0.5m, lado: 15.5m, quiet: 0.5m))))).Should().Contain("zona de silêncio");

    [Fact]
    public void TextoInvadindoAZonaDoQr_Recusa()
    {
        var invasor = Texto("seq", "001/012", 42m, 15m, 10m, 3.44m, 7.5m);

        Recusa(Pimaco(0, Etiqueta(PimacoCompleta(extras: invasor)))).Should().Contain("'seq' invade a zona de silêncio");
    }

    [Fact]
    public void BarrasSemZonaDeSilencioDe10Modulos_Recusa() =>
        Recusa(Pimaco(0, Etiqueta(PimacoCompleta(barras: Barras(x: 0m, w: 32m, quiet: 0.5m))))).Should().Contain("zona de silêncio");

    [Fact]
    public void BarrasComModuloAbaixoDe025mm_Recusa() =>
        Recusa(Pimaco(0, Etiqueta(PimacoCompleta(barras: Barras(x: 4m, w: 30m, quiet: 0m))))).Should().Contain("módulo");

    [Fact]
    public void BarrasAbaixoDe8mm_Recusa() =>
        Recusa(Pimaco(0, Etiqueta(PimacoCompleta(barras: Barras(h: 7.5m))))).Should().Contain("8 mm");

    [Fact]
    public void TipoImagem_AindaNaoSaiNoPdf()
    {
        var logo = new JsonObject
        {
            ["id"] = "logo", ["type"] = "image", ["asset"] = "loja:logo",
            ["x_mm"] = 50m, ["y_mm"] = 30m, ["w_mm"] = 5m, ["h_mm"] = 5m,
        };

        Recusa(Pimaco(0, Etiqueta(PimacoCompleta(extras: logo)))).Should().Contain("ainda não sai no PDF");
    }

    // ---------- Folha e limites ----------

    [Fact]
    public void LayoutQueNaoCabeNaPosicao_Recusa() =>
        Recusa(Pimaco(0, Etiqueta(Simples(70m, 40m, "horizontal")))).Should().Contain("não cabe na posição");

    [Fact]
    public void QuantasCabem_TamanhosDiferentes_Recusa() =>
        Recusa(QuantasCabem(Etiqueta(Simples(52m, 75m, "vertical")), Etiqueta(Simples(50m, 30m, "horizontal"))))
            .Should().Contain("mesmo tamanho");

    [Fact]
    public void ModeloDesconhecido_Recusa() =>
        Recusa(new GerarFolhaEtiquetasPdfCommand(new FolhaSpec("a4-generica", null, null, null), false, 0,
            [Etiqueta(PimacoCompleta())])).Should().Contain("folha.modelo");

    [Fact]
    public void OrientacaoDaFolhaDiferenteDoLayout_Recusa() =>
        Recusa(new GerarFolhaEtiquetasPdfCommand(new FolhaSpec("pimaco-a4256", "vertical", null, null), false, 0,
            [Etiqueta(PimacoCompleta())])).Should().Contain("a folha pede vertical");

    [Fact]
    public void PularTodaAFolha_Recusa() =>
        Recusa(Pimaco(21, Etiqueta(PimacoCompleta()))).Should().Contain("pular");

    [Fact]
    public void MaisDe500Etiquetas_Recusa() =>
        Recusa(Pimaco(0, Etiqueta(PimacoCompleta(), copias: 300), Etiqueta(PimacoCompleta(), copias: 201)))
            .Should().Contain("500");

    [Fact]
    public void MaisDe50Paginas_Recusa() =>
        Recusa(QuantasCabem(Etiqueta(Simples(150m, 250m, "vertical"), copias: 51))).Should().Contain("50 páginas");

    [Fact]
    public void SemEtiquetas_Recusa() =>
        Recusa(Pimaco(0)).Should().Contain("etiquetas");
}

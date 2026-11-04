using System.Globalization;
using System.Text;
using EasyStock.Application.Ports.Output.Pdf;
using EasyStock.Application.UseCases.Etiquetas.Folha;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace EasyStock.Infra.Async.Pdf;

/// <summary>
/// Folha A4 de etiquetas em PDF vetorial (#2200). Cada elemento vai na posição absoluta em mm do plano; QR e barras
/// saem como caminho SVG (não bitmap), texto em Lato numa linha só, cortado com reticências.
/// </summary>
public sealed class FolhaEtiquetasPdfRenderer : IFolhaEtiquetasPdfRenderer
{
    private const float MmPorPt = 25.4f / 72f;
    /// <summary>Altura natural da linha da Lato (ascendente + descendente + entrelinha), em em.</summary>
    private const float AlturaLinhaEm = 1.2f;
    private const float DivisoriaPt = 0.5f;
    private const float GuiaTracoMm = 0.1f;
    private const string Fonte = "Lato";
    private const string Preto = "#000000";

    static FolhaEtiquetasPdfRenderer() => QuestPDF.Settings.License = LicenseType.Community;

    /// <summary>Síncrono e pesado; quem limita quantos rodam ao mesmo tempo é a <see cref="VagasDeFolhaPdf"/> do use case.</summary>
    public Task<byte[]> RenderAsync(PlanoFolhaEtiquetas plano, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plano);
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Gerar(plano));
    }

    private static byte[] Gerar(PlanoFolhaEtiquetas plano)
    {
        var svgs = new Dictionary<object, SvgImage>();
        return Document.Create(documento =>
        {
            foreach (var pagina in plano.Paginas)
                documento.Page(page =>
                {
                    page.Size(F(plano.PaginaWMm), F(plano.PaginaHMm), Unit.Millimetre);
                    page.Margin(0);
                    page.Content().Layers(camadas =>
                    {
                        camadas.PrimaryLayer().Extend();
                        foreach (var celula in pagina.Celulas)
                        {
                            if (plano.Guias) Guia(camadas.Layer(), celula.Grade, svgs);
                            if (celula.Etiqueta is not null) Etiqueta(camadas.Layer(), celula, celula.Etiqueta, svgs);
                        }
                    });
                });
        }).GeneratePdf();
    }

    private static void Etiqueta(IContainer camada, CelulaPlano celula, EtiquetaPlano etiqueta, Dictionary<object, SvgImage> svgs)
    {
        var (w, h) = celula.Girar ? (etiqueta.HMm, etiqueta.WMm) : (etiqueta.WMm, etiqueta.HMm);
        var caixa = Em(camada, celula.EtiquetaXMm, celula.EtiquetaYMm, w).Height(F(h), Unit.Millimetre);
        // Gira 90° no sentido horário: o canto superior esquerdo da etiqueta vai para o canto superior direito da posição.
        if (celula.Girar) caixa = caixa.RotateRight();
        caixa.Layers(camadas =>
        {
            camadas.PrimaryLayer().Extend();
            foreach (var elemento in etiqueta.Elementos)
                Elemento(camadas.Layer(), elemento, svgs);
        });
    }

    private static void Elemento(IContainer camada, ElementoPlano elemento, Dictionary<object, SvgImage> svgs)
    {
        switch (elemento)
        {
            case TextoPlano t:
                Texto(camada, t);
                break;
            case QrPlano q:
                Em(camada, q.XMm, q.YMm, q.LadoMm).Height(F(q.LadoMm), Unit.Millimetre).Svg(Svg(svgs, q.Matriz, () => SvgQr(q.Matriz))).FitArea();
                break;
            case BarrasPlano b:
                Em(camada, b.XMm, b.YMm, b.WMm).Height(F(b.HMm), Unit.Millimetre).Svg(Svg(svgs, SvgBarras(b))).FitArea();
                break;
            case LinhaPlano l:
                var meio = F(l.YMm) + F(l.HMm) / 2 - DivisoriaPt * MmPorPt / 2;
                camada.PaddingLeft(F(l.XMm), Unit.Millimetre).PaddingTop(meio, Unit.Millimetre).Unconstrained()
                    .Width(F(l.WMm), Unit.Millimetre).LineHorizontal(DivisoriaPt).LineColor(Preto);
                break;
        }
    }

    /// <summary>Uma linha, centrada na altura da caixa; o que passar da largura vira reticências.</summary>
    private static void Texto(IContainer camada, TextoPlano t)
    {
        var pt = F(t.TamanhoPt);
        var topo = Math.Max(0f, F(t.YMm) + (F(t.HMm) - pt * MmPorPt * AlturaLinhaEm) / 2);
        camada.PaddingLeft(F(t.XMm), Unit.Millimetre).PaddingTop(topo, Unit.Millimetre).Unconstrained()
            .Width(F(t.WMm), Unit.Millimetre)
            .Text(texto =>
            {
                texto.ClampLines(1, "…");
                switch (t.Alinhamento)
                {
                    case AlinhamentoTexto.Centro: texto.AlignCenter(); break;
                    case AlinhamentoTexto.Direita: texto.AlignRight(); break;
                    default: texto.AlignLeft(); break;
                }
                var span = texto.Span(t.Texto.ReplaceLineEndings(" ")).FontFamily(Fonte).FontSize(pt).FontColor(Preto);
                if (t.Peso >= 700) span.Bold();
                else if (t.Peso >= 600) span.SemiBold();
                else if (t.Peso >= 500) span.Medium();
            });
    }

    private static void Guia(IContainer camada, CelulaGrade celula, Dictionary<object, SvgImage> svgs)
    {
        var w = F(celula.WMm);
        var h = F(celula.HMm);
        var meio = GuiaTracoMm / 2;
        var svg = string.Create(CultureInfo.InvariantCulture,
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{w}\" height=\"{h}\" viewBox=\"0 0 {w} {h}\">" +
            $"<rect x=\"{meio}\" y=\"{meio}\" width=\"{w - GuiaTracoMm}\" height=\"{h - GuiaTracoMm}\" fill=\"none\" " +
            $"stroke=\"#999999\" stroke-width=\"{GuiaTracoMm}\" stroke-dasharray=\"1 0.8\"/></svg>");
        Em(camada, celula.XMm, celula.YMm, celula.WMm).Height(h, Unit.Millimetre).Svg(Svg(svgs, svg)).FitArea();
    }

    /// <summary>Um retângulo por trecho contínuo de módulos escuros em cada linha, num caminho só (sem frestas).</summary>
    private static string SvgQr(bool[,] matriz)
    {
        var n = matriz.GetLength(0);
        var sb = new StringBuilder(n * n * 4);
        sb.Append(CultureInfo.InvariantCulture,
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{n}\" height=\"{n}\" viewBox=\"0 0 {n} {n}\" shape-rendering=\"crispEdges\"><path fill=\"#000000\" d=\"");
        for (var linha = 0; linha < n; linha++)
            for (var coluna = 0; coluna < n; coluna++)
            {
                if (!matriz[linha, coluna]) continue;
                var inicio = coluna;
                while (coluna + 1 < n && matriz[linha, coluna + 1]) coluna++;
                var largura = coluna - inicio + 1;
                sb.Append(CultureInfo.InvariantCulture, $"M{inicio} {linha}h{largura}v1h-{largura}z");
            }
        return sb.Append("\"/></svg>").ToString();
    }

    /// <summary>Unidade do SVG = 1 módulo; a altura entra na mesma escala para a caixa não distorcer.</summary>
    private static string SvgBarras(BarrasPlano barras)
    {
        var total = barras.Modulos.Length;
        var altura = (barras.HMm * total / barras.WMm).ToString("0.####", CultureInfo.InvariantCulture);
        var sb = new StringBuilder(total * 4);
        sb.Append(CultureInfo.InvariantCulture,
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{total}\" height=\"{altura}\" viewBox=\"0 0 {total} {altura}\" shape-rendering=\"crispEdges\"><path fill=\"#000000\" d=\"");
        for (var i = 0; i < total; i++)
        {
            if (barras.Modulos[i] != '1') continue;
            var inicio = i;
            while (i + 1 < total && barras.Modulos[i + 1] == '1') i++;
            sb.Append(CultureInfo.InvariantCulture, $"M{inicio} 0h{i - inicio + 1}v{altura}h-{i - inicio + 1}z");
        }
        return sb.Append("\"/></svg>").ToString();
    }

    private static IContainer Em(IContainer camada, decimal x, decimal y, decimal w) =>
        camada.PaddingLeft(F(x), Unit.Millimetre).PaddingTop(F(y), Unit.Millimetre).Unconstrained().Width(F(w), Unit.Millimetre);

    private static SvgImage Svg(Dictionary<object, SvgImage> cache, string svg) => Svg(cache, svg, () => svg);

    /// <summary>
    /// Uma imagem por <paramref name="chave"/>: o texto do SVG ou, no QR, a própria matriz (a mesma instância serve a
    /// todas as cópias do código), assim o SVG do QR só é montado uma vez.
    /// </summary>
    private static SvgImage Svg(Dictionary<object, SvgImage> cache, object chave, Func<string> svg)
    {
        if (!cache.TryGetValue(chave, out var imagem)) cache[chave] = imagem = SvgImage.FromText(svg());
        return imagem;
    }

    private static float F(decimal mm) => (float)mm;
}

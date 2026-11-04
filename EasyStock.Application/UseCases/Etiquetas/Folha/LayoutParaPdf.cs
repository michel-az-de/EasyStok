using System.Globalization;
using System.Text.RegularExpressions;
using EasyStock.Application.Ports.Output.Pdf;

namespace EasyStock.Application.UseCases.Etiquetas.Folha;

/// <summary>
/// Confere um LayoutJson v1 já resolvido contra os pisos de leitura da folha A4 e o converte em
/// <see cref="EtiquetaPlano"/>. Os pisos são os da impressão (o que sai legível e escaneável no papel), não os do
/// editor: o resolvedor encolhe fonte e código até esses valores, abaixo dos 8 pt e 18 mm que o editor aceita.
/// </summary>
public static class LayoutParaPdf
{
    public const decimal NomeMinPt = 10m;
    public const decimal ValidadeMinPt = 9m;
    public const decimal DadoMinPt = 7.5m;
    public const decimal QrModuloMinMm = 0.375m;
    public const int QrZonaModulos = 4;
    public const decimal BarrasModuloMinMm = 0.25m;
    public const decimal BarrasAlturaMinMm = 8m;
    public const int MaxElementos = 40;
    public const int MaxCodigos = 2;
    public const int MaxTexto = 500;
    public const decimal MmPorPt = 25.4m / 72m;

    private const decimal Tol = 0.01m;
    private const decimal TolQuadrado = 0.05m;

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly Regex Variavel = new(@"\{[a-z]+(?:\.[a-z_A-Z]+)*(?::[^}]+)?\}", RegexOptions.Compiled);
    private static readonly HashSet<string> Fontes = ["display", "sans", "mono"];
    private static readonly HashSet<string> TiposSemPdf = ["image", "nutritional-table", "preparo-text", "alergenos-pills"];

    private sealed record Caixa(decimal X, decimal Y, decimal W, decimal H)
    {
        public bool Sobrepoe(Caixa o) =>
            X < o.X + o.W - Tol && o.X < X + W - Tol && Y < o.Y + o.H - Tol && o.Y < Y + H - Tol;
    }

    /// <summary>Piso de fonte pelo prefixo do id: <c>nome*</c> 10 pt, <c>val*</c> 9 pt, o resto 7,5 pt.</summary>
    public static decimal PisoPt(string id) =>
        id.StartsWith("nome", StringComparison.OrdinalIgnoreCase) ? NomeMinPt
        : id.StartsWith("val", StringComparison.OrdinalIgnoreCase) ? ValidadeMinPt
        : DadoMinPt;

    /// <summary>Converte ou devolve nulo, acrescentando a <paramref name="erros"/> cada motivo de recusa.</summary>
    public static EtiquetaPlano? Converter(LayoutJsonDocument? layout, IQrCodeEncoder qr, ICollection<string> erros, string rotulo)
    {
        var antes = erros.Count;
        void Erro(FormattableString msg) => erros.Add(rotulo + ": " + msg.ToString(PtBr));

        if (layout is null) { Erro($"layout ausente."); return null; }
        if (layout.V != 1) Erro($"versão do layout deve ser 1.");
        var tamanho = layout.Size;
        if (tamanho is null || tamanho.WMm <= 0 || tamanho.HMm <= 0) { Erro($"size.w_mm e size.h_mm devem ser maiores que zero."); return null; }
        if (tamanho.Orientation is not ("horizontal" or "vertical")) Erro($"size.orientation deve ser 'horizontal' ou 'vertical'.");
        var elementos = layout.Elements;
        if (elementos is null || elementos.Count == 0) { Erro($"elements não pode estar vazio."); return null; }
        if (elementos.Count > MaxElementos) { Erro($"no máximo {MaxElementos} elementos."); return null; }
        if (elementos.Count(e => e?.Type == "code") > MaxCodigos) Erro($"no máximo {MaxCodigos} códigos por etiqueta.");

        var larguraEtq = tamanho.WMm;
        var alturaEtq = tamanho.HMm;
        var plano = new List<ElementoPlano>(elementos.Count);
        var caixas = new List<Caixa>(elementos.Count);
        var zonas = new List<(int Indice, string Id, Caixa Zona)>();

        for (var i = 0; i < elementos.Count; i++)
        {
            var e = elementos[i];
            if (e is null) { Erro($"elemento {i + 1} vazio."); caixas.Add(new Caixa(0, 0, 0, 0)); continue; }
            var id = string.IsNullOrWhiteSpace(e.Id) ? $"#{i + 1}" : e.Id;
            void ErroEl(FormattableString msg) => Erro($"elemento '{id}' {msg}");
            caixas.Add(new Caixa(e.XMm, e.YMm, e.WMm, e.HMm));

            if (string.IsNullOrWhiteSpace(e.Id)) ErroEl($"sem id.");
            if (e.WMm <= 0 || e.HMm <= 0) { ErroEl($"precisa de largura e altura maiores que zero."); continue; }
            if (e.XMm < -Tol || e.YMm < -Tol || e.XMm + e.WMm > larguraEtq + Tol || e.YMm + e.HMm > alturaEtq + Tol)
                ErroEl($"sai da etiqueta de {larguraEtq:0.##} x {alturaEtq:0.##} mm.");

            switch (e.Type)
            {
                case "text":
                    var texto = Texto(e, ErroEl);
                    if (texto is not null) plano.Add(texto);
                    break;
                case "code":
                    var codigo = Codigo(e, larguraEtq, alturaEtq, qr, ErroEl, out var zona);
                    if (codigo is not null) plano.Add(codigo);
                    if (zona is not null) zonas.Add((i, id, zona));
                    break;
                case "divider":
                    plano.Add(new LinhaPlano(e.XMm, e.YMm, e.WMm, e.HMm));
                    break;
                case { } tipo when TiposSemPdf.Contains(tipo):
                    ErroEl($"do tipo '{tipo}' ainda não sai no PDF.");
                    break;
                default:
                    ErroEl($"tem tipo '{e.Type}' não permitido.");
                    break;
            }
        }

        foreach (var (indice, id, zona) in zonas)
            for (var j = 0; j < caixas.Count; j++)
                if (j != indice && zona.Sobrepoe(caixas[j]))
                    Erro($"elemento '{elementos[j]?.Id}' invade a zona de silêncio do código '{id}'.");

        return erros.Count == antes ? new EtiquetaPlano(larguraEtq, alturaEtq, plano) : null;
    }

    private static TextoPlano? Texto(LayoutElement e, Action<FormattableString> erro)
    {
        var ok = ConteudoValido(e.Content, erro);
        if (e.Font is not null && !Fontes.Contains(e.Font)) { erro($"tem font fora de display, sans e mono."); ok = false; }
        AlinhamentoTexto alinhamento;
        switch (e.Align)
        {
            case null or "left": alinhamento = AlinhamentoTexto.Esquerda; break;
            case "center": alinhamento = AlinhamentoTexto.Centro; break;
            case "right": alinhamento = AlinhamentoTexto.Direita; break;
            default: erro($"tem align fora de left, center e right."); ok = false; alinhamento = default; break;
        }

        if (e.SizePt is not { } pt) { erro($"sem size_pt."); return null; }
        var piso = PisoPt(e.Id ?? "");
        if (pt < piso) { erro($"tem {pt:0.##} pt, abaixo do mínimo de {piso:0.##} pt."); ok = false; }
        if (pt * MmPorPt > e.HMm + Tol) { erro($"tem {pt:0.##} pt, maior que a altura de {e.HMm:0.##} mm."); ok = false; }
        var peso = e.Weight ?? 400;
        if (peso is < 100 or > 900) { erro($"tem weight fora de 100 a 900."); ok = false; }

        return ok ? new TextoPlano(e.XMm, e.YMm, e.WMm, e.HMm, e.Content!, pt, peso, alinhamento) : null;
    }

    private static ElementoPlano? Codigo(LayoutElement e, decimal larguraEtq, decimal alturaEtq, IQrCodeEncoder qr, Action<FormattableString> erro, out Caixa? zona)
    {
        zona = null;
        if (!ConteudoValido(e.Content, erro)) return null;
        var conteudo = e.Content!;
        var q = e.QuietZoneMm ?? 0m;
        if (q < 0) { erro($"tem quiet_zone_mm negativa."); return null; }

        switch (e.Format)
        {
            case "qr":
            {
                if (Math.Abs(e.WMm - e.HMm) > TolQuadrado) { erro($"QR precisa de caixa quadrada."); return null; }
                var matriz = qr.Matriz(conteudo);
                var n = matriz.GetLength(0);
                var lado = Math.Min(e.WMm, e.HMm) - 2 * q;
                var modulo = lado / n;
                if (modulo < QrModuloMinMm)
                {
                    erro($"QR com módulo de {modulo:0.###} mm, abaixo do mínimo de {QrModuloMinMm} mm.");
                    return null;
                }
                var x = e.XMm + (e.WMm - lado) / 2;
                var y = e.YMm + (e.HMm - lado) / 2;
                var zonaMm = QrZonaModulos * modulo;
                zona = new Caixa(x - zonaMm, y - zonaMm, lado + 2 * zonaMm, lado + 2 * zonaMm);
                if (!Dentro(zona, larguraEtq, alturaEtq))
                {
                    erro($"QR sem a zona de silêncio de {QrZonaModulos} módulos ({zonaMm:0.##} mm) dentro da etiqueta.");
                    zona = null;
                    return null;
                }
                return new QrPlano(x, y, lado, matriz);
            }
            case "barcode-code128":
            {
                var problema = Code128.Problema(conteudo);
                if (problema is not null) { erro($"CODE128 inválido: {problema}."); return null; }
                var modulos = Code128.Codificar(conteudo).Modulos;
                var largura = e.WMm - 2 * q;
                var modulo = largura / modulos.Length;
                var ok = true;
                if (modulo < BarrasModuloMinMm)
                {
                    erro($"código de barras com módulo de {modulo:0.###} mm, abaixo do mínimo de {BarrasModuloMinMm} mm.");
                    ok = false;
                }
                if (e.HMm < BarrasAlturaMinMm)
                {
                    erro($"código de barras com {e.HMm:0.##} mm de altura, abaixo do mínimo de {BarrasAlturaMinMm} mm.");
                    ok = false;
                }
                if (!ok) return null;
                var zonaMm = Code128.ZonaSilencioModulos * modulo;
                zona = new Caixa(e.XMm + q - zonaMm, e.YMm, largura + 2 * zonaMm, e.HMm);
                if (!Dentro(zona, larguraEtq, alturaEtq))
                {
                    erro($"código de barras sem a zona de silêncio de {Code128.ZonaSilencioModulos} módulos ({zonaMm:0.##} mm) dentro da etiqueta.");
                    zona = null;
                    return null;
                }
                return new BarrasPlano(e.XMm + q, e.YMm, largura, e.HMm, modulos);
            }
            case "barcode-ean13":
                erro($"EAN-13 exige GTIN registrado na GS1 e ainda não sai no PDF; use barcode-code128.");
                return null;
            default:
                erro($"tem format '{e.Format}' não permitido.");
                return null;
        }
    }

    private static bool ConteudoValido(string? conteudo, Action<FormattableString> erro)
    {
        if (string.IsNullOrWhiteSpace(conteudo)) { erro($"sem content."); return false; }
        if (conteudo.Length > MaxTexto) { erro($"com content acima de {MaxTexto} caracteres."); return false; }
        var variavel = Variavel.Match(conteudo);
        if (variavel.Success) { erro($"com variável não resolvida {variavel.Value}."); return false; }
        return true;
    }

    private static bool Dentro(Caixa c, decimal larguraEtq, decimal alturaEtq) =>
        c.X >= -Tol && c.Y >= -Tol && c.X + c.W <= larguraEtq + Tol && c.Y + c.H <= alturaEtq + Tol;
}

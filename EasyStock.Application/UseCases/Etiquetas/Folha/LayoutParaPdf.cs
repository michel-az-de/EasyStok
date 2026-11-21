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

    /// <summary>Maior tamanho ou coordenada aceito, em mm. Acima disso a conta com decimal estoura em vez de recusar.</summary>
    public const decimal MaxDimensaoMm = 1000m;

    /// <summary>Quantos caracteres de um valor do pedido voltam numa mensagem de erro.</summary>
    public const int MaxEco = 40;

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

    /// <summary>
    /// Valor vindo do pedido, curto e sem quebra de linha, para voltar numa mensagem de erro sem espelhar um texto
    /// de qualquer tamanho.
    /// </summary>
    public static string Eco(string? valor, int max = MaxEco)
    {
        if (string.IsNullOrEmpty(valor)) return "";
        var corte = Math.Min(valor.Length, max);
        if (corte < valor.Length && corte > 0 && char.IsHighSurrogate(valor[corte - 1])) corte--;
        var limpo = string.Create(corte, valor, (destino, origem) =>
        {
            for (var i = 0; i < destino.Length; i++) destino[i] = char.IsControl(origem[i]) ? ' ' : origem[i];
        });
        return corte < valor.Length ? limpo + "…" : limpo;
    }

    /// <summary>Confere que a etiqueta tem largura e altura entre zero (exclusivo) e <see cref="MaxDimensaoMm"/>.</summary>
    public static bool TamanhoValido(LayoutJsonDocument layout, ICollection<string> erros, string rotulo)
    {
        var tamanho = layout.Size;
        if (tamanho is null || tamanho.WMm <= 0 || tamanho.HMm <= 0)
        {
            erros.Add($"{rotulo}: size.w_mm e size.h_mm devem ser maiores que zero.");
            return false;
        }
        if (tamanho.WMm > MaxDimensaoMm || tamanho.HMm > MaxDimensaoMm)
        {
            erros.Add(string.Create(PtBr, $"{rotulo}: size.w_mm e size.h_mm devem ser no máximo {MaxDimensaoMm:0} mm."));
            return false;
        }
        return true;
    }

    /// <summary>Caracteres dos elementos de texto da etiqueta, para o teto de texto do pedido.</summary>
    public static long CaracteresDeTexto(LayoutJsonDocument layout) =>
        layout.Elements?.Where(e => e?.Type == "text").Sum(e => (long)(e.Content?.Length ?? 0)) ?? 0;

    /// <summary>Acrescenta a <paramref name="distintos"/> o par (formato, conteúdo) de cada código da etiqueta.</summary>
    public static void CodigosDe(LayoutJsonDocument layout, ISet<(string?, string?)> distintos)
    {
        if (layout.Elements is null) return;
        foreach (var e in layout.Elements)
            if (e?.Type == "code" && !string.IsNullOrWhiteSpace(e.Content))
                distintos.Add((e.Format, e.Content));
    }

    /// <summary>
    /// Acrescenta a <paramref name="distintos"/> cada caractere de texto que a Lato não desenha. Para de contar
    /// depois de <paramref name="limite"/>: o pedido já vai ser recusado.
    /// </summary>
    public static void GlifosForaDoLatino(LayoutJsonDocument layout, ISet<int> distintos, int limite)
    {
        if (layout.Elements is null) return;
        foreach (var e in layout.Elements)
        {
            if (e?.Type != "text" || e.Content is null) continue;
            foreach (var caractere in e.Content.EnumerateRunes())
            {
                if (distintos.Count > limite) return;
                if (!DesenhadoPelaLato(caractere.Value)) distintos.Add(caractere.Value);
            }
        }
    }

    /// <summary>Latim básico, Latin-1 e Latin Extended-A, a pontuação tipográfica (travessão, aspas, reticências), € e ™.</summary>
    private static bool DesenhadoPelaLato(int c) => c <= 0x017F || c is >= 0x2010 and <= 0x203A or 0x20AC or 0x2122;

    /// <summary>Converte ou devolve nulo, acrescentando a <paramref name="erros"/> cada motivo de recusa.</summary>
    public static EtiquetaPlano? Converter(LayoutJsonDocument? layout, IQrCodeEncoder qr, ICollection<string> erros, string rotulo) =>
        Converter(layout, new CodigosDoPedido(qr), erros, rotulo);

    /// <summary>
    /// Converte ou devolve nulo, acrescentando a <paramref name="erros"/> cada motivo de recusa. Os QR e as barras
    /// saem de <paramref name="codigos"/>, que gera cada conteúdo uma vez só no pedido.
    /// </summary>
    public static EtiquetaPlano? Converter(LayoutJsonDocument? layout, CodigosDoPedido codigos, ICollection<string> erros, string rotulo)
    {
        var antes = erros.Count;
        void Erro(FormattableString msg) => erros.Add(rotulo + ": " + msg.ToString(PtBr));

        if (layout is null) { Erro($"layout ausente."); return null; }
        if (layout.V != 1) Erro($"versão do layout deve ser 1.");
        if (!TamanhoValido(layout, erros, rotulo)) return null;
        var tamanho = layout.Size;
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
            var id = string.IsNullOrWhiteSpace(e.Id) ? $"#{i + 1}" : Eco(e.Id);
            void ErroEl(FormattableString msg) => Erro($"elemento '{id}' {msg}");

            if (string.IsNullOrWhiteSpace(e.Id)) ErroEl($"sem id.");
            if (ForaDoLimite(e.XMm) || ForaDoLimite(e.YMm) || ForaDoLimite(e.WMm) || ForaDoLimite(e.HMm) || ForaDoLimite(e.QuietZoneMm ?? 0m))
            {
                caixas.Add(new Caixa(0, 0, 0, 0));
                ErroEl($"tem x_mm, y_mm, w_mm, h_mm ou quiet_zone_mm fora do limite de {MaxDimensaoMm:0} mm.");
                continue;
            }
            caixas.Add(new Caixa(e.XMm, e.YMm, e.WMm, e.HMm));

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
                    var codigo = Codigo(e, larguraEtq, alturaEtq, codigos, ErroEl, out var zona);
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
                    ErroEl($"tem tipo '{Eco(e.Type)}' não permitido.");
                    break;
            }
        }

        foreach (var (indice, id, zona) in zonas)
            for (var j = 0; j < caixas.Count; j++)
                if (j != indice && zona.Sobrepoe(caixas[j]))
                    Erro($"elemento '{Eco(elementos[j]?.Id)}' invade a zona de silêncio do código '{id}'.");

        return erros.Count == antes ? new EtiquetaPlano(larguraEtq, alturaEtq, plano) : null;
    }

    private static bool ForaDoLimite(decimal mm) => mm is < -MaxDimensaoMm or > MaxDimensaoMm;

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

    private static ElementoPlano? Codigo(LayoutElement e, decimal larguraEtq, decimal alturaEtq, CodigosDoPedido codigos, Action<FormattableString> erro, out Caixa? zona)
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
                var matriz = codigos.Qr(conteudo);
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
                var modulos = codigos.Barras(conteudo);
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
                erro($"tem format '{Eco(e.Format)}' não permitido.");
                return null;
        }
    }

    private static bool ConteudoValido(string? conteudo, Action<FormattableString> erro)
    {
        if (string.IsNullOrWhiteSpace(conteudo)) { erro($"sem content."); return false; }
        if (conteudo.Length > MaxTexto) { erro($"com content acima de {MaxTexto} caracteres."); return false; }
        var variavel = Variavel.Match(conteudo);
        if (variavel.Success) { erro($"com variável não resolvida {Eco(variavel.Value)}."); return false; }
        return true;
    }

    private static bool Dentro(Caixa c, decimal larguraEtq, decimal alturaEtq) =>
        c.X >= -Tol && c.Y >= -Tol && c.X + c.W <= larguraEtq + Tol && c.Y + c.H <= alturaEtq + Tol;
}

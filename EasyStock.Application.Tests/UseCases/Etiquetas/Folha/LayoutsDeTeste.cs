using System.Text.Json;
using System.Text.Json.Nodes;
using EasyStock.Application.Ports.Output.Pdf;
using EasyStock.Application.UseCases.Etiquetas.Folha;

namespace EasyStock.Application.Tests.UseCases.Etiquetas.Folha;

/// <summary>
/// Layouts resolvidos como o resolvedor do protótipo entrega para a Pimaco A4256 (63,5 x 38,1 mm, horizontal, QR e
/// barras), já nos pisos: nome 10 pt, validade 9 pt, dados 7,5 pt, QR com módulo 0,5 mm e barras com 0,25 mm.
/// </summary>
internal static class LayoutsDeTeste
{
    public const string ConteudoQr = "TALHARIM-LOT-261103-707082-001";
    public const string ConteudoBarras = "2611037070820001";

    public static JsonObject Texto(string id, string conteudo, decimal x, decimal y, decimal w, decimal h, decimal pt,
        int peso = 400, string fonte = "sans", string align = "left") => new()
    {
        ["id"] = id, ["type"] = "text", ["content"] = conteudo, ["font"] = fonte, ["size_pt"] = pt,
        ["weight"] = peso, ["align"] = align, ["overflow"] = "clip",
        ["x_mm"] = x, ["y_mm"] = y, ["w_mm"] = w, ["h_mm"] = h,
    };

    public static JsonObject Codigo(string id, string formato, string conteudo, decimal x, decimal y, decimal w, decimal h,
        decimal quiet) => new()
    {
        ["id"] = id, ["type"] = "code", ["format"] = formato, ["content"] = conteudo, ["quiet_zone_mm"] = quiet,
        ["x_mm"] = x, ["y_mm"] = y, ["w_mm"] = w, ["h_mm"] = h,
    };

    public static JsonObject Qr(decimal x = 45m, decimal y = 3m, decimal lado = 15.5m, decimal quiet = 0.5m) =>
        Codigo("qr", "qr", ConteudoQr, x, y, lado, lado, quiet);

    public static JsonObject Barras(decimal x = 4.125m, decimal y = 25.16m, decimal w = 35.75m, decimal h = 8m, decimal quiet = 2.5m) =>
        Codigo("barras", "barcode-code128", ConteudoBarras, x, y, w, h, quiet);

    public static JsonObject Layout(decimal w, decimal h, string orientacao, params JsonObject[] elementos) => new()
    {
        ["v"] = 1,
        ["size"] = new JsonObject { ["preset"] = "custom", ["w_mm"] = w, ["h_mm"] = h, ["orientation"] = orientacao },
        ["elements"] = new JsonArray(elementos.Select(JsonNode (e) => e).ToArray()),
    };

    /// <summary>Pimaco horizontal com QR e barras, como a folha 1 do protótipo.</summary>
    public static JsonObject PimacoCompleta(
        decimal nomePt = 10m, decimal valPt = 9m, decimal lotePt = 7.5m, JsonObject? qr = null, JsonObject? barras = null,
        string nome = "Talharim Fresco de Espinafre", params JsonObject[] extras) =>
        Layout(63.5m, 38.1m, "horizontal",
        [
            Texto("empresa", "Casa da Baba", 1.5m, 1.5m, 41m, 3.53m, 8m, 600),
            Texto("nome", nome, 1.5m, 5.03m, 41m, 4.06m, nomePt, 700),
            Texto("val", "Val 06/11/2026", 1.5m, 9.5m, 41m, 4.13m, valPt, 700),
            Texto("lote", "LOT-261103-707082", 1.5m, 14m, 41m, 3.44m, lotePt, 700, "mono"),
            qr ?? Qr(),
            barras ?? Barras(),
            Texto("hri", ConteudoBarras, 4.125m, 33.16m, 35.75m, 3.44m, 7.5m, 400, "mono", "center"),
            .. extras,
        ]);

    /// <summary>Etiqueta só com o nome, no tamanho pedido.</summary>
    public static JsonObject Simples(decimal w, decimal h, string orientacao) =>
        Layout(w, h, orientacao, Texto("nome", "Talharim", 1m, 1m, w - 2m, 5m, 10m, 700));

    public static EtiquetaNaFolha Etiqueta(JsonObject layout, int? copias = null) =>
        new(JsonSerializer.SerializeToElement(layout), copias);
}

/// <summary>QR falso com 29 x 29 módulos (versão 3), o tamanho que o modo byte dá para <see cref="LayoutsDeTeste.ConteudoQr"/>.</summary>
internal sealed class QrFalso(int modulos = 29) : IQrCodeEncoder
{
    public bool[,] Matriz(string conteudo) => new bool[modulos, modulos];
}

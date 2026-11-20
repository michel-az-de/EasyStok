using QuestPDF.Drawing;

namespace EasyStock.Infra.Async.Pdf;

/// <summary>
/// Registra uma vez as fontes da marca embutidas no assembly (o servidor não as tem instaladas). Lora nos títulos e
/// Nunito Sans no corpo, em instâncias estáticas sob um nome só: o QuestPDF escolhe pelo peso de cada arquivo.
/// </summary>
internal static class FontesDaMarca
{
    private static readonly Lazy<bool> Registradas = new(() =>
    {
        RegistrarFonte("Lora.ttf");
        foreach (var peso in new[] { "Regular", "SemiBold", "Bold" })
            RegistrarFonte($"NunitoSans-{peso}.ttf", MarcaDaCasa.FonteCorpo);
        return true;
    });

    public static void Registrar() => _ = Registradas.Value;

    private static void RegistrarFonte(string arquivo, string? familia = null)
    {
        var nome = $"EasyStock.Infra.Async.Pdf.Fontes.{arquivo}";
        using var fonte = typeof(FontesDaMarca).Assembly.GetManifestResourceStream(nome)
            ?? throw new InvalidOperationException($"Fonte da marca ausente no assembly: {nome}.");
        if (familia is null) FontManager.RegisterFont(fonte);
        else FontManager.RegisterFontWithCustomName(familia, fonte);
    }
}

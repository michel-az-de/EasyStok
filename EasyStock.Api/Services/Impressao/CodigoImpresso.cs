namespace EasyStock.Api.Services.Impressao;

public static class CodigoImpresso
{
    public static readonly IReadOnlyList<string> Tipos = ["qr", "barras"];
    public static string? Gerar(string tipo, string numero, string? link) => tipo switch
    {
        "barras" => Code128Svg.Gerar(numero),
        "qr" => link is null ? null : QrSvg.Gerar(link),
        _ => throw new ArgumentException("Código desconhecido.", nameof(tipo)),
    };

    public static string AjustarPagina(string html, string codigo) => codigo == "barras"
        ? html.Replace("</style>", ".im .qrl{flex-wrap:wrap}.im .qrl .barras{width:48mm;max-width:100%;height:14mm;flex:none;background:white}</style>", StringComparison.Ordinal)
        : html;
}

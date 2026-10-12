using System.Globalization;
using System.Text;

namespace EasyStock.Api.Services.Impressao;

/// <summary>
/// CODE128 (conjunto B) em SVG, gerado no servidor para o impresso sair igual em qualquer consumidor e o
/// snapshot ser determinístico (S49). Uma unidade do <c>viewBox</c> = um módulo; a largura real vem do CSS.
/// Zona de silêncio de <see cref="ZonaSilencio"/> módulos de cada lado.
/// </summary>
public static class Code128Svg
{
    public const int ZonaSilencio = 10;
    private const int StartB = 104;
    private const int Stop = 106;

    /// <summary>Larguras (barra, espaço, barra...) dos símbolos 0 a 106 da norma ISO/IEC 15417.</summary>
    private static readonly string[] Padroes =
    [
        "212222", "222122", "222221", "121223", "121322", "131222", "122213", "122312", "132212", "221213",
        "221312", "231212", "112232", "122132", "122231", "113222", "123122", "123221", "223211", "221132",
        "221231", "213212", "223112", "312131", "311222", "321122", "321221", "312212", "322112", "322211",
        "212123", "212321", "232121", "111323", "131123", "131321", "112313", "132113", "132311", "211313",
        "231113", "231311", "112133", "112331", "132131", "113123", "113321", "133121", "313121", "211331",
        "231131", "213113", "213311", "213131", "311123", "311321", "331121", "312113", "312311", "332111",
        "314111", "221411", "431111", "111224", "111422", "121124", "121421", "141122", "141221", "112214",
        "112412", "122114", "122411", "142112", "142211", "241211", "221114", "413111", "241112", "134111",
        "111242", "121142", "121241", "114212", "124112", "124211", "411212", "421112", "421211", "212141",
        "214121", "412121", "111143", "111341", "131141", "114113", "114311", "411113", "411311", "113141",
        "114131", "311141", "411131", "211412", "211214", "211232", "2331112",
    ];

    /// <summary>Módulos (1 = barra, 0 = espaço), sem a zona de silêncio. ASCII 32 a 126.</summary>
    public static string Modulos(string valor)
    {
        ArgumentException.ThrowIfNullOrEmpty(valor);
        var simbolos = new List<int>(valor.Length + 3) { StartB };
        foreach (var ch in valor)
        {
            if (ch is < ' ' or > '~')
                throw new ArgumentException($"Caractere fora do CODE128 B: U+{(int)ch:X4}.", nameof(valor));
            simbolos.Add(ch - ' ');
        }
        var soma = StartB;
        for (var i = 1; i < simbolos.Count; i++) soma += simbolos[i] * i;
        simbolos.Add(soma % 103);
        simbolos.Add(Stop);

        var sb = new StringBuilder(simbolos.Count * 11 + 2);
        foreach (var s in simbolos)
        {
            var padrao = Padroes[s];
            for (var i = 0; i < padrao.Length; i++)
                sb.Append(i % 2 == 0 ? '1' : '0', padrao[i] - '0');
        }
        return sb.ToString();
    }

    /// <summary>SVG com barras pretas sobre fundo transparente, altura de <paramref name="altura"/> módulos.</summary>
    public static string Gerar(string valor, int altura = 40)
    {
        var modulos = Modulos(valor);
        var largura = modulos.Length + 2 * ZonaSilencio;
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture,
            $"<svg class=\"barras\" xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {largura} {altura}\" preserveAspectRatio=\"none\" shape-rendering=\"crispEdges\" role=\"img\" aria-label=\"Código de barras {valor}\">");
        var i = 0;
        while (i < modulos.Length)
        {
            if (modulos[i] == '0') { i++; continue; }
            var inicio = i;
            while (i < modulos.Length && modulos[i] == '1') i++;
            sb.Append(CultureInfo.InvariantCulture,
                $"<rect x=\"{inicio + ZonaSilencio}\" y=\"0\" width=\"{i - inicio}\" height=\"{altura}\" fill=\"#000\"/>");
        }
        sb.Append("</svg>");
        return sb.ToString();
    }
}

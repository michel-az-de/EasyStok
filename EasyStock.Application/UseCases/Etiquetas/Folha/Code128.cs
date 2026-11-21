using System.Text;

namespace EasyStock.Application.UseCases.Etiquetas.Folha;

/// <summary>Símbolos (início, dados, verificador e parada) e módulos (1 = barra, 0 = espaço) sem a zona de silêncio.</summary>
public sealed record Code128Codificado(char Conjunto, IReadOnlyList<int> Simbolos, string Modulos)
{
    public int Verificador => Simbolos[^2];
}

/// <summary>
/// CODE128 (ISO/IEC 15417). Conjunto C quando o valor é só dígitos em quantidade par (dois por símbolo, metade da
/// largura); conjunto B nos demais casos (ASCII 32 a 126).
/// </summary>
public static class Code128
{
    public const int ZonaSilencioModulos = 10;
    public const int TamanhoMaximo = 80;
    private const int StartB = 104;
    private const int StartC = 105;
    private const int Stop = 106;

    /// <summary>Larguras (barra, espaço, barra...) dos símbolos 0 a 106.</summary>
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

    /// <summary>Por que o valor não vira CODE128, ou nulo quando vira.</summary>
    public static string? Problema(string? valor)
    {
        if (string.IsNullOrEmpty(valor)) return "valor vazio";
        if (valor.Length > TamanhoMaximo) return $"mais de {TamanhoMaximo} caracteres";
        foreach (var ch in valor)
            if (ch is < ' ' or > '~')
                return $"caractere fora do CODE128 (U+{(int)ch:X4})";
        return null;
    }

    public static Code128Codificado Codificar(string valor)
    {
        var problema = Problema(valor);
        if (problema is not null) throw new ArgumentException($"CODE128: {problema}.", nameof(valor));

        var conjuntoC = valor.Length % 2 == 0 && valor.All(char.IsAsciiDigit);
        var simbolos = new List<int>(valor.Length + 3) { conjuntoC ? StartC : StartB };
        if (conjuntoC)
            for (var i = 0; i < valor.Length; i += 2)
                simbolos.Add((valor[i] - '0') * 10 + (valor[i + 1] - '0'));
        else
            simbolos.AddRange(valor.Select(ch => ch - ' '));

        var soma = simbolos[0];
        for (var i = 1; i < simbolos.Count; i++) soma += simbolos[i] * i;
        simbolos.Add(soma % 103);
        simbolos.Add(Stop);

        var modulos = new StringBuilder(simbolos.Count * 11 + 2);
        foreach (var simbolo in simbolos)
        {
            var padrao = Padroes[simbolo];
            for (var i = 0; i < padrao.Length; i++)
                modulos.Append(i % 2 == 0 ? '1' : '0', padrao[i] - '0');
        }
        return new Code128Codificado(conjuntoC ? 'C' : 'B', simbolos, modulos.ToString());
    }
}

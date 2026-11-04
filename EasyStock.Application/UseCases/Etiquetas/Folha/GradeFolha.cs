namespace EasyStock.Application.UseCases.Etiquetas.Folha;

/// <summary>Uma posição da grade, em mm a partir do canto superior esquerdo da página.</summary>
public sealed record CelulaGrade(decimal XMm, decimal YMm, decimal WMm, decimal HMm);

/// <summary>
/// Grade de etiquetas numa A4 retrato, posições em ordem de leitura (linha a linha, da esquerda para a direita).
/// </summary>
public sealed class GradeFolha
{
    public const decimal A4LarguraMm = 210m;
    public const decimal A4AlturaMm = 297m;

    private GradeFolha(int colunas, int linhas, IReadOnlyList<CelulaGrade> celulas)
    {
        Colunas = colunas;
        Linhas = linhas;
        Celulas = celulas;
    }

    public int Colunas { get; }
    public int Linhas { get; }
    public IReadOnlyList<CelulaGrade> Celulas { get; }
    public int PorPagina => Celulas.Count;

    /// <summary>
    /// Pimaco A4256: 21 etiquetas de 63,5 x 38,1 mm em 3 colunas e 7 linhas, margem esquerda 7,21 mm, topo
    /// 15,15 mm, passo 66,04 mm na horizontal e 38,1 mm na vertical (gabarito do fabricante).
    /// </summary>
    public static GradeFolha PimacoA4256() =>
        Montar(3, 7, 63.5m, 38.1m, 7.21m, 15.15m, 66.04m, 38.1m);

    /// <summary>
    /// Quantas etiquetas de <paramref name="wMm"/> x <paramref name="hMm"/> cabem na A4 retrato com a margem e o
    /// espaço pedidos, grade centralizada na página. Nulo quando nem uma cabe.
    /// </summary>
    public static GradeFolha? QuantasCabem(decimal wMm, decimal hMm, decimal margemMm, decimal espacoMm)
    {
        if (wMm <= 0 || hMm <= 0) return null;
        var colunas = (int)Math.Floor((A4LarguraMm - 2 * margemMm + espacoMm) / (wMm + espacoMm));
        var linhas = (int)Math.Floor((A4AlturaMm - 2 * margemMm + espacoMm) / (hMm + espacoMm));
        if (colunas < 1 || linhas < 1) return null;

        var esquerda = (A4LarguraMm - (colunas * wMm + (colunas - 1) * espacoMm)) / 2;
        var topo = (A4AlturaMm - (linhas * hMm + (linhas - 1) * espacoMm)) / 2;
        return Montar(colunas, linhas, wMm, hMm, esquerda, topo, wMm + espacoMm, hMm + espacoMm);
    }

    private static GradeFolha Montar(int colunas, int linhas, decimal w, decimal h,
        decimal esquerda, decimal topo, decimal passoX, decimal passoY)
    {
        var celulas = new List<CelulaGrade>(colunas * linhas);
        for (var linha = 0; linha < linhas; linha++)
            for (var coluna = 0; coluna < colunas; coluna++)
                celulas.Add(new CelulaGrade(esquerda + coluna * passoX, topo + linha * passoY, w, h));
        return new GradeFolha(colunas, linhas, celulas);
    }
}

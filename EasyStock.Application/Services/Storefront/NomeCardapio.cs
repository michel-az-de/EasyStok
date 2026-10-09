namespace EasyStock.Application.Services.Storefront;

/// <summary>
/// Nome do item do cardápio como o cliente vê na vitrine. Saiu de <c>ListarCardapioPublicoUseCase</c> para o
/// resumo do pedido na conversa mostrar o item igual ao cardápio (#1474).
/// </summary>
public static class NomeCardapio
{
    /// <summary>Preposições/conjunções que ficam minúsculas no meio do título (pt-BR).</summary>
    private static readonly HashSet<string> PalavrasMinusculas = new(StringComparer.Ordinal)
    {
        "de", "da", "do", "das", "dos", "e", "com", "a", "o", "ao", "aos",
        "à", "às", "em", "no", "na", "nos", "nas", "para", "sem", "por", "ou",
    };

    /// <summary>Unidades que seguem um número e ficam minúsculas ("500 g", "250 ml"), #1334.</summary>
    private static readonly HashSet<string> UnidadesDeMedida = new(StringComparer.Ordinal)
    {
        "g", "kg", "ml", "l",
    };

    /// <summary>
    /// Title-case pt-BR para exibição na vitrine. <c>NomePublico</c>/<c>CategoriaTexto</c> de
    /// itens avulsos são armazenados em minúsculo (factory). Capitaliza a 1ª letra de cada
    /// palavra (e após hífen), deixando preposições do meio minúsculas. <strong>Guard:</strong>
    /// só transforma se o valor vier TODO minúsculo — preserva nomes de Produto (itens
    /// vinculados) que já vêm capitalizados.
    /// </summary>
    public static string? Exibicao(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return s;
        if (s != s.ToLowerInvariant()) return s; // já tem maiúscula → preserva (ex: nome de Produto)

        var palavras = s.Split(' ');
        for (var i = 0; i < palavras.Length; i++)
        {
            var p = palavras[i];
            if (p.Length == 0) continue;
            if (i > 0 && PalavrasMinusculas.Contains(p)) continue;
            if (i > 0 && UnidadesDeMedida.Contains(p) && palavras[i - 1].Length > 0 && char.IsDigit(palavras[i - 1][^1])) continue;

            var arr = p.ToCharArray();
            var capitalizar = true;
            for (var j = 0; j < arr.Length; j++)
            {
                if (arr[j] == '-') { capitalizar = true; continue; }
                if (capitalizar && char.IsLetter(arr[j]))
                {
                    arr[j] = char.ToUpperInvariant(arr[j]);
                    capitalizar = false;
                }
            }
            palavras[i] = new string(arr);
        }
        return string.Join(' ', palavras);
    }
}

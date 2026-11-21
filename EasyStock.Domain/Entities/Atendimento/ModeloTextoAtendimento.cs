using System.Text.RegularExpressions;

namespace EasyStock.Domain.Entities.Atendimento;

/// <summary>
/// Renderização das variáveis das respostas prontas e automáticas (S42): <c>{nome}</c>, <c>{pedido}</c> e
/// <c>{faixa}</c>. Chave fora dessa lista é recusada ao salvar (<see cref="ChavesDesconhecidas"/>, #2781); o texto
/// antigo que ainda a tenha fica como está ao renderizar.
/// </summary>
public static partial class ModeloTextoAtendimento
{
    public static readonly IReadOnlyList<string> Variaveis = ["nome", "pedido", "faixa"];

    public static string Renderizar(string texto, IReadOnlyDictionary<string, string?> valores)
    {
        ArgumentNullException.ThrowIfNull(texto);
        ArgumentNullException.ThrowIfNull(valores);

        return Marcador().Replace(texto, m =>
        {
            var nome = m.Groups[1].Value;
            if (!Variaveis.Contains(nome, StringComparer.Ordinal)) return m.Value;
            return valores.TryGetValue(nome, out var valor) && !string.IsNullOrWhiteSpace(valor)
                ? valor.Trim()
                : throw new VariavelSemValorException(nome);
        });
    }

    /// <summary>
    /// #2781: recusa ao salvar a chave entre chaves que não é variável (<c>{nome_errado}</c>, <c>{Nome}</c>): sem isso
    /// ela iria ao cliente com as chaves à mostra. Devolve a mensagem de erro com as chaves erradas, ou nulo.
    /// </summary>
    public static string? ChavesDesconhecidas(string? texto)
    {
        var erradas = MarcadorLivre().Matches(texto ?? string.Empty)
            .Select(m => m.Groups[1].Value)
            .Where(chave => !Variaveis.Contains(chave, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Select(chave => $"{{{chave}}}")
            .ToList();
        return erradas.Count == 0
            ? null
            : $"Variável desconhecida no texto: {string.Join(", ", erradas)}. Use só {string.Join(", ", Variaveis.Select(v => $"{{{v}}}"))}.";
    }

    [GeneratedRegex(@"\{([a-z]+)\}")]
    private static partial Regex Marcador();

    [GeneratedRegex(@"\{([\p{L}\p{N}_]+)\}")]
    private static partial Regex MarcadorLivre();
}

using System.Text.RegularExpressions;

namespace EasyStock.Domain.Entities.Atendimento;

/// <summary>Variável usada no texto sem valor na conversa (S42): nunca sai <c>{nome}</c> literal.</summary>
public sealed class VariavelSemValorException(string variavel)
    : RegraDeDominioVioladaException($"A variável {{{variavel}}} não tem valor nesta conversa.")
{
    public string Variavel { get; } = variavel;
}

/// <summary>
/// Renderização das variáveis das respostas prontas e automáticas (S42): <c>{nome}</c>, <c>{pedido}</c> e
/// <c>{faixa}</c>. Chave fora dessa lista fica como está (pode ser texto da dona entre chaves).
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

    [GeneratedRegex(@"\{([a-z]+)\}")]
    private static partial Regex Marcador();
}

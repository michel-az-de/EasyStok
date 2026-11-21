using System.Text.Json;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// #2853: ferramentas do Assistente que a loja desligou, guardadas como lista de nomes em
/// <c>ConfiguracaoAtendimento.FerramentasDesligadasJson</c>. Lista de negação: ferramenta nova nasce ligada.
/// As saídas para a equipe ficam <see cref="Travadas"/>: sem elas o cliente ficaria sem caminho até uma pessoa.
/// </summary>
public static class FerramentasDaLoja
{
    public static readonly IReadOnlySet<string> Travadas = new HashSet<string>(StringComparer.Ordinal)
    {
        "escalar_para_dona",
        "encaminhar_para_responsavel",
        "avisar_gestao",
    };

    private static readonly IReadOnlySet<string> Nenhuma = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Nomes desligados; JSON nulo, vazio ou ilegível vale "tudo ligado".</summary>
    public static IReadOnlySet<string> DaLoja(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Nenhuma;
        try
        {
            var nomes = JsonSerializer.Deserialize<string[]>(json);
            return nomes is null or { Length: 0 }
                ? Nenhuma
                : nomes.Where(n => !string.IsNullOrWhiteSpace(n) && !Travadas.Contains(n)).ToHashSet(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return Nenhuma;
        }
    }

    /// <summary>Lista ordenada e sem repetição; vazia serializa como vazio (volta ao padrão: tudo ligado).</summary>
    public static string Serializar(IEnumerable<string> nomes)
    {
        var lista = nomes.Select(n => n.Trim()).Where(n => n.Length > 0).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
        return lista.Length == 0 ? string.Empty : JsonSerializer.Serialize(lista);
    }

    /// <summary>Mensagem de recusa, ou nulo quando todos os nomes existem e nenhum está travado.</summary>
    public static string? Erro(IEnumerable<string> nomes, IEnumerable<string> conhecidas)
    {
        var existentes = conhecidas.ToHashSet(StringComparer.Ordinal);
        foreach (var nome in nomes.Select(n => n.Trim()))
        {
            if (!existentes.Contains(nome)) return $"Ferramenta desconhecida: {nome}.";
            if (Travadas.Contains(nome)) return $"A ferramenta {nome} não pode ser desligada: é o caminho do cliente até a equipe.";
        }
        return null;
    }
}

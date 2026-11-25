using System.Text.RegularExpressions;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// #3340 (R2I-05): o cliente cobra dinheiro ou fala em Procon. A conversa vai à dona sem chamar o modelo, que não tem
/// como prometer nada disso. Critério determinístico no texto da mensagem, como a desistência (#2992).
/// </summary>
public static partial class DetectorReclamacaoCliente
{
    public static bool EhReclamacao(IEnumerable<string?> textos) =>
        textos.Any(t => !string.IsNullOrWhiteSpace(t) && Padrao().IsMatch(t));

    // Cobrança, não consulta: "quero/preciso/exijo ... estorno", o verbo no imperativo ("estorne", "reembolsem"), "dinheiro de
    // volta" e Procon. "Meu estorno foi confirmado?" é pergunta de status, e o assistente a responde pelo histórico do pedido.
    [GeneratedRegex(@"\b(procon|dinheiro de volta|estorne\w*|reembolse\w*|(quero|preciso|exijo|exigimos)\b[^.?!]{0,30}\b(estorno|reembolso))\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Padrao();
}

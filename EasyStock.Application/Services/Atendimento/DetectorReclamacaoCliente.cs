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

    // Cobrança, não consulta: "quero/preciso/exijo/vou pedir ... estorno" (e "estornar", "reembolsada"), o verbo no imperativo
    // ("estorne", "reembolsem"), a demora ("não recebi o reembolso", "o dinheiro não voltou"), "dinheiro de volta" e Procon.
    // "Quero saber/ver/conferir/o status/me avisem ... estorno" e "Meu estorno foi confirmado?" são perguntas de status, e o
    // assistente as responde pelo histórico do pedido: a palavra de consulta só vale ANTES do estorno, na mesma oração. As janelas
    // (30, 20 e 15 caracteres) param na pontuação, vírgula inclusive. "Dinheiro" só conta quando é o do cliente ("meu/o/do
    // dinheiro", "caiu dinheiro"), não a forma de pagamento ("em dinheiro").
    [GeneratedRegex(@"\b(procon|dinheiro de volta|estorne\w*|reembolse\w*|(quero|preciso|exijo|exigimos|vou pedir|vou solicitar)\b(?!(?:(?!estorn|reembols)[^.?!,;]){0,30}\b(?:saber|ver|consultar|perguntar|conferir|checar|confirm\w*|status|acompanhar|avis\w*|informa\w*)\b)[^.?!,;]{0,30}\b(estorn\w*|reembols\w*)|n[aã]o (caiu|voltou|devolveram)\b[^.?!,;]{0,20}\b(estorno|reembolso|dinheiro)|n[aã]o recebi\b[^.?!,;]{0,20}\b(estorno|reembolso|(meu|o|do) dinheiro)|(estorno|reembolso)\b[^.?!,;]{0,20}\bn[aã]o (voltou|caiu|chegou)|(meu|o) dinheiro\b[^.?!,;]{0,15}\bn[aã]o (voltou|caiu|chegou)|dinheiro\s+(ainda\s+)?n[aã]o (voltou|caiu|chegou))\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Padrao();
}

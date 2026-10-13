using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// Guarda do texto livre do agente (#1704). A conversa (cumprimento, dúvida, pedir endereço) vai ao cliente;
/// o que afirma pedido, pagamento, devolução, frete grátis, valor ou link só passa quando veio de uma fonte do
/// turno: resultado de ferramenta, prompt da loja ou histórico da conversa. Devolve o motivo da barragem, ou
/// <c>null</c> quando o texto pode sair. Na dúvida barra: quem responde é a equipe.
/// </summary>
public static partial class GuardaRespostaAgente
{
    /// <summary>Quantidade máxima aceita ao conferir "N unidades de um preço da fonte".</summary>
    private const int MaximoDeUnidades = 50;

    public static string? Avaliar(string? texto, IEnumerable<string> fontes, IEnumerable<string> nomesDeFerramentas)
    {
        if (string.IsNullOrWhiteSpace(texto)) return "o agente não produziu resposta";

        var plano = SemAcento(texto);
        var inicio = plano.TrimStart();
        if (inicio.StartsWith('{') || inicio.StartsWith('[') || plano.Contains("```", StringComparison.Ordinal)
            || nomesDeFerramentas.Any(n => plano.Contains(n, StringComparison.OrdinalIgnoreCase)))
            return "texto com chamada de ferramenta no lugar da resposta";

        foreach (var frase in Frases().Split(plano))
        {
            if (AfirmaSemCondicao(frase, PedidoFeito()) || AfirmaSemCondicao(frase, FizPedido()))
                return "afirma situação do pedido sem a ferramenta confirmar";
            if (AfirmaSemCondicao(frase, PagamentoFeito()) || AfirmaSemCondicao(frase, RecebemosPagamento()))
                return "afirma pagamento ou devolução sem a ferramenta confirmar";
            if (Cortesia().Match(frase) is { Success: true } c && !Negacao().IsMatch(frase[..c.Index]))
                return "promete frete grátis ou desconto, que é decisão da equipe";
        }

        var conhecidas = fontes.Select(SemAcento).ToList();
        var valores = conhecidas.SelectMany(f => Valor().Matches(f)).Select(Reais).ToHashSet();
        foreach (Match m in Valor().Matches(plano))
        {
            var valor = Reais(m);
            if (!valores.Contains(valor) && !valores.Any(v => v > 0 && valor % v == 0 && valor / v is > 1 and <= MaximoDeUnidades))
                return $"valor {m.Value.Replace("r$", "R$", StringComparison.Ordinal)} sem origem em ferramenta";
        }

        foreach (Match m in Link().Matches(plano))
        {
            var link = m.Value.TrimEnd('.', ',', ';', ':', '!', '?', ')');
            if (!conhecidas.Any(f => f.Contains(link, StringComparison.Ordinal)))
                return "link sem origem em ferramenta";
        }

        return null;
    }

    /// <summary>"quando o pedido for confirmado" é condição, não afirmação.</summary>
    private static bool AfirmaSemCondicao(string frase, Regex afirmacao) =>
        afirmacao.Match(frase) is { Success: true } m && !Condicao().IsMatch(frase[..m.Index]);

    private static decimal Reais(Match m)
    {
        var numero = m.Groups[1].Value;
        // "1.234,56" e "1234,5" são reais; "38.00" (ponto decimal) também aparece em resultado de ferramenta.
        numero = numero.Contains(',') ? numero.Replace(".", string.Empty).Replace(',', '.')
            : PontoDeMilhar().IsMatch(numero) ? numero.Replace(".", string.Empty) : numero;
        return decimal.TryParse(numero, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : -1;
    }

    private static string SemAcento(string texto)
    {
        var sb = new StringBuilder(texto.Length);
        foreach (var c in texto.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(c));
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    [GeneratedRegex(@"[.!?\n]+(?=\s|$)")]
    private static partial Regex Frases();

    [GeneratedRegex(@"\bpedido\b.{0,40}\b(registrado|criado|confirmado|fechado|anotado|realizado|gerado|efetuado|finalizado|concluido|cancelado|alterado|atualizado|pago|pronto|entregue|saiu|a caminho)\b")]
    private static partial Regex PedidoFeito();

    [GeneratedRegex(@"\b(registrei|criei|confirmei|fechei|anotei|gerei|finalizei|registramos|criamos|fechamos|anotamos)\b.{0,30}\bpedido\b")]
    private static partial Regex FizPedido();

    [GeneratedRegex(@"\b(pagamento|pix|estorno|reembolso|devolucao|credito)\b.{0,40}\b(confirmad[oa]|aprovad[oa]|recebid[oa]|identificad[oa]|caiu|feit[oa]|realizad[oa]|processad[oa]|efetuad[oa]|concluid[oa]|liberad[oa])\b")]
    private static partial Regex PagamentoFeito();

    [GeneratedRegex(@"\b(recebemos|recebi|confirmamos|identificamos|estornamos|devolvemos)\b.{0,30}\b(pagamento|pix|valor|dinheiro)\b")]
    private static partial Regex RecebemosPagamento();

    [GeneratedRegex(@"\b(frete|entrega|taxa)\b.{0,25}\b(gratis|gratuit[oa]|cortesia|por nossa conta)\b|\bdesconto de\b|\b\d{1,2}\s?% de desconto\b")]
    private static partial Regex Cortesia();

    [GeneratedRegex(@"\b(quando|assim que|depois que|apos|se|caso)\b")]
    private static partial Regex Condicao();

    [GeneratedRegex(@"\b(nao|nunca|sem)\b")]
    private static partial Regex Negacao();

    [GeneratedRegex(@"r\$\s*(\d{1,3}(?:\.\d{3})+(?:,\d{1,2})?|\d+(?:[.,]\d{1,2})?)")]
    private static partial Regex Valor();

    [GeneratedRegex(@"^\d{1,3}(\.\d{3})+$")]
    private static partial Regex PontoDeMilhar();

    [GeneratedRegex(@"https?://[^\s<>\]""']+")]
    private static partial Regex Link();
}

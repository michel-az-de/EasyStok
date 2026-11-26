using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// Guarda do texto livre do agente (#1704). A conversa (cumprimento, dúvida, pedir endereço) vai ao cliente;
/// o que afirma pedido, pagamento, devolução, frete grátis, valor ou link só passa quando veio de uma fonte do
/// turno: resultado de ferramenta. Devolve o motivo da barragem, ou
/// <c>null</c> quando o texto pode sair. Na dúvida barra: quem responde é a equipe.
/// </summary>
public static partial class GuardaRespostaAgente
{
    private const int MaximoQuantidade = 50;

    /// <param name="nomeDaDona">#3278 (R4): <c>ConfiguracaoAtendimento.NomeDona</c>. Promessa feita em nome dela ("a Fulana vai
    /// combinar com você") conta como promessa da equipe. Sem nome, valem só os papéis (dona, equipe, responsável, atendente, ela).</param>
    public static string? Avaliar(string? texto, IEnumerable<string> fontes, IEnumerable<string> nomesDeFerramentas, string? nomeDaDona = null,
        IEnumerable<string>? ferramentasOk = null)
    {
        if (string.IsNullOrWhiteSpace(texto)) return "o agente não produziu resposta";

        var plano = SemAcento(texto);
        var inicio = plano.TrimStart();
        if (inicio.StartsWith('{') || inicio.StartsWith('[') || plano.Contains("```", StringComparison.Ordinal)
            || nomesDeFerramentas.Any(n => plano.Contains(n, StringComparison.OrdinalIgnoreCase)))
            return "texto com chamada de ferramenta no lugar da resposta";

        var originais = fontes.ToList();
        var conhecidas = originais.Select(SemAcento).ToList();
        var feitas = ferramentasOk?.ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>(StringComparer.Ordinal);
        // #2516: "frete combinado" só passa quando a ferramenta mostra o frete combinado.
        var acaoDaEquipe = AcaoDaEquipeDe(nomeDaDona);
        var freteCombinadoNaFonte = conhecidas.Any(f => f.Contains("fretecombinado", StringComparison.Ordinal) || FreteCombinado().IsMatch(f));

        foreach (var frase in Frases().Split(plano))
        {
            if (Encaminhamento().IsMatch(frase) || AfirmaSemNegacao(frase, acaoDaEquipe))
                return "promete encaminhamento sem acionar a equipe";
            if (AfirmaAcaoSemFerramenta(frase, AvisoFeito(), feitas, "escalar_para_dona", "avisar_gestao", "encaminhar_para_responsavel"))
                return "promete encaminhamento sem acionar a equipe";
            if (AfirmaAcaoSemFerramenta(frase, EnvioComanda(), feitas, "enviar_cardapio_imagem")
                || AfirmaAcaoSemFerramenta(frase, EnvioResumo(), feitas, "enviar_resumo_pedido")
                || AfirmaAcaoSemFerramenta(frase, EnvioLink(), feitas, "enviar_link_pagamento", "enviar_cardapio_imagem"))
                return "afirma envio sem a ferramenta confirmar";
            if (AfirmaSemCondicao(frase, PedidoFeito()) || AfirmaSemCondicao(frase, FizPedido()))
                return "afirma situação do pedido sem a ferramenta confirmar";
            if (AfirmaSemCondicao(frase, PagamentoFeito()) || AfirmaSemCondicao(frase, RecebemosPagamento()) || AfirmaSemNegacao(frase, Estorno()))
                return "afirma pagamento ou devolução sem a ferramenta confirmar";
            if (Cortesia().Match(frase) is { Success: true } c && !Negacao().IsMatch(frase[..c.Index]))
                return "promete frete grátis ou desconto, que é decisão da equipe";
            if (!freteCombinadoNaFonte && AfirmaSemNegacao(frase, FreteCombinado()))
                return "afirma frete combinado sem a ferramenta confirmar";
        }

        var valores = conhecidas.SelectMany(f => Valor().Matches(f)).Select(Reais).ToHashSet();
        foreach (Match m in Valor().Matches(plano))
        {
            var valor = Reais(m);
            // #3379: preço da ferramenta vezes quantidade inteira (2 a 50) é o total da conta, não valor inventado.
            if (!valores.Contains(valor) && !valores.Any(v => v > 0 && valor % v == 0 && valor / v is >= 2 and <= MaximoQuantidade))
                return $"valor {m.Value.Replace("r$", "R$", StringComparison.Ordinal)} sem origem em ferramenta";
        }

        var links = originais.SelectMany(f => Link().Matches(f).Cast<Match>())
            .Select(m => m.Value.TrimEnd('.', ',', ';', ':', '!', '?', ')')).ToHashSet(StringComparer.Ordinal);
        foreach (Match m in Link().Matches(texto))
        {
            var link = m.Value.TrimEnd('.', ',', ';', ':', '!', '?', ')');
            if (!links.Contains(link))
                return "link sem origem em ferramenta";
        }

        return null;
    }

    /// <summary>"quando o pedido for confirmado" é condição, não afirmação.</summary>
    private static bool AfirmaSemCondicao(string frase, Regex afirmacao) =>
        afirmacao.Match(frase) is { Success: true } m && !(Condicao().IsMatch(frase[..m.Index]) && Futuro().IsMatch(m.Value));

    /// <summary>#2516: "o frete ainda não foi combinado" e "a equipe não vai ligar" negam, não afirmam.</summary>
    private static bool AfirmaSemNegacao(string frase, Regex afirmacao) =>
        AfirmaSemCondicao(frase, afirmacao) && afirmacao.Match(frase) is var m && !Nao().IsMatch(frase[..(m.Index + m.Length)]);

    /// <summary>#3340 (R2I-01, R2I-05): ação que só vale se alguma das ferramentas dela rodou com ok no turno.</summary>
    private static bool AfirmaAcaoSemFerramenta(string frase, Regex afirmacao, ISet<string> feitas, params string[] ferramentas) =>
        AfirmaSemNegacao(frase, afirmacao) && !ferramentas.Any(f => feitas.Contains(f));

    private static decimal Reais(Match m)
    {
        var numero = m.Groups["valor"].Value;
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

    [GeneratedRegex(@"[.!?\n]+(?=\s|$)|,\s+|\s+mas\s+")]
    private static partial Regex Frases();

    [GeneratedRegex(@"\b(pedido|encomenda|compra)\b.{0,40}\b(registrad[oa]|criad[oa]|confirmad[oa]|fechad[oa]|anotad[oa]|realizad[oa]|gerad[oa]|efetuad[oa]|finalizad[oa]|concluid[oa]|cancelad[oa]|alterad[oa]|atualizad[oa]|pag[oa]|pront[oa]|entregue|saiu|a caminho)\b")]
    private static partial Regex PedidoFeito();

    [GeneratedRegex(@"\b(registrei|criei|confirmei|fechei|anotei|gerei|finalizei|registramos|criamos|fechamos|anotamos)\b.{0,30}\b(pedido|encomenda|compra)\b")]
    private static partial Regex FizPedido();

    [GeneratedRegex(@"\b(pagamento|pix|estorno|reembolso|devolucao|credito)\b.{0,40}\b(confirmad[oa]|aprovad[oa]|recebid[oa]|identificad[oa]|caiu|feit[oa]|realizad[oa]|processad[oa]|efetuad[oa]|concluid[oa]|liberad[oa])\b")]
    private static partial Regex PagamentoFeito();

    [GeneratedRegex(@"\b(recebemos|recebi|confirmamos|identificamos|estornamos|devolvemos)\b.{0,30}\b(pagamento|pix|valor|dinheiro)\b")]
    private static partial Regex RecebemosPagamento();

    [GeneratedRegex(@"\b(vou|irei|ja)\s+(pedir|chamar|avisar|encaminhar|passar|transferir|verificar|conferir)\b.{0,60}\b(dona|equipe|atendente|humano)\b")]
    private static partial Regex Encaminhamento();

    // #2516: "a dona vai combinar com você a entrega e o frete" sem escalar_para_dona. #3278: o nome da dona vem da configuração
    // da loja (AcaoDaEquipeDe); aqui os papéis. "baba" fica porque é a marca da Casa da Baba e o modelo a usa como sujeito
    // ("a Baba vai combinar...", frase real da #2516) mesmo com NomeDona vazio ou diferente.
    private const string Acoes = "combinar|chamar|ligar|retornar|responder|confirmar|verificar|resolver|acertar|avaliar|analisar|entrar em contato|falar";
    private const string PapeisDaEquipe = "dona|equipe|responsavel|atendente|ela|baba";
    private const string VerboDaAcao = @"\b(vai|vao|ira|irao)\s+(te\s+|lhe\s+)?(" + Acoes + @")\b";

    [GeneratedRegex(@"\b(" + PapeisDaEquipe + @")\b.{0,20}" + VerboDaAcao)]
    private static partial Regex AcaoDaEquipe();

    private const int LimiteDoCacheDeNomes = 256;
    private static readonly ConcurrentDictionary<string, Regex> AcoesPorNome = new(StringComparer.Ordinal);

    /// <summary>
    /// #3278: a regra dos papéis com o nome da dona na alternância. O nome vem sem acento e sem caixa, escapado, e só casa como
    /// palavra inteira ("Ana" não casa em "banana"). O primeiro nome também conta ("Maria Silva" -> "a Maria vai te chamar").
    /// </summary>
    private static Regex AcaoDaEquipeDe(string? nomeDaDona)
    {
        var nomes = NomesDaDona(nomeDaDona);
        if (nomes.Count == 0) return AcaoDaEquipe();
        var chave = string.Join('|', nomes);
        if (AcoesPorNome.TryGetValue(chave, out var pronta)) return pronta;
        var alternancia = string.Join('|', nomes.Select(Regex.Escape));
        var regex = new Regex(@"(?:\b(?:" + PapeisDaEquipe + @")\b|(?<!\w)(?:" + alternancia + @")(?!\w)).{0,20}" + VerboDaAcao,
            RegexOptions.CultureInvariant);
        if (AcoesPorNome.Count < LimiteDoCacheDeNomes) AcoesPorNome.TryAdd(chave, regex);
        return regex;
    }

    private static List<string> NomesDaDona(string? nomeDaDona)
    {
        if (string.IsNullOrWhiteSpace(nomeDaDona)) return [];
        var nome = string.Join(' ', SemAcento(nomeDaDona).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var nomes = new List<string> { nome };
        var primeiro = nome.Split(' ')[0];
        if (primeiro.Length >= 2 && primeiro != nome) nomes.Add(primeiro);
        return nomes;
    }

    // Só frete ou taxa acordados: "a entrega está definida para amanhã" e "a taxa é definida pelo CEP" são conversa.
    [GeneratedRegex(@"\b(avisei|avisamos|chamei|encaminhei|passei)\b.{0,30}\b(equipe|dona|responsavel|atendente|humano|gestao)\b")]
    private static partial Regex AvisoFeito();

    [GeneratedRegex(@"\b(mandei|enviei|reenviei|mandamos|enviamos|reenviamos)\b.{0,30}\b(comanda|cardapio)\b")]
    private static partial Regex EnvioComanda();

    [GeneratedRegex(@"\b(mandei|enviei|reenviei|mandamos|enviamos|reenviamos)\b.{0,30}\bresumo\b")]
    private static partial Regex EnvioResumo();

    [GeneratedRegex(@"\b(mandei|enviei|reenviei|mandamos|enviamos|reenviamos)\b.{0,30}\blink\b")]
    private static partial Regex EnvioLink();

    // #3340 (R2I-05): nenhuma ferramenta devolve dinheiro, então a promessa de estorno ou devolução barra sempre.
    [GeneratedRegex(@"\b(estornei|estornamos|reembolsei|reembolsamos|ressarci)\b|\b(vou|vamos|irei|ja)\s+(providenciar|estornar|devolver|reembolsar|ressarcir)\b.{0,25}\b(estorno|dinheiro|valor|reembolso|devolucao)\b|\b(dinheiro|valor|estorno|reembolso)\b.{0,25}\b(sera|vai ser|ficara)\s+devolvid[oa]\b")]
    private static partial Regex Estorno();

    [GeneratedRegex(@"\b(frete|taxa)\b.{0,25}\b(combinad[oa]|acertad[oa])\b")]
    private static partial Regex FreteCombinado();

    [GeneratedRegex(@"\b(nao|nunca)\b")]
    private static partial Regex Nao();

    [GeneratedRegex(@"\b(frete|entrega|taxa)\b.{0,25}\b(gratis|gratuit[oa]|cortesia|por nossa conta)\b|\bdesconto\b")]
    private static partial Regex Cortesia();

    [GeneratedRegex(@"^\s*(quando|assim que|depois que|apos|se|caso)\b")]
    private static partial Regex Condicao();

    [GeneratedRegex(@"\b(for|estiver|tiver|seja)\b")]
    private static partial Regex Futuro();

    [GeneratedRegex(@"^\s*(nao|nunca)\s+(temos|oferecemos|fazemos|damos|ha)\b")]
    private static partial Regex Negacao();

    [GeneratedRegex(@"(?:r\$\s*(?<valor>\d{1,3}(?:\.\d{3})+(?:,\d{1,2})?|\d+(?:[.,]\d{1,2})?)|(?<valor>\d+(?:[.,]\d{1,2})?)\s*reais\b)")]
    private static partial Regex Valor();

    [GeneratedRegex(@"^\d{1,3}(\.\d{3})+$")]
    private static partial Regex PontoDeMilhar();

    [GeneratedRegex(@"https?://[^\s<>\]""']+")]
    private static partial Regex Link();
}

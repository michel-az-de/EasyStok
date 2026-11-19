using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using EasyStock.Application.Services.Storefront;
using EasyStock.Application.Services.Pedidos;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Storefront;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// Texto do pedido da conversa: itens, frete, total, entrega e forma de pagamento. Serve à nota do
/// cardápio do site (S48) e ao resumo que a operadora manda ao cliente pelo console (F03).
/// </summary>
public static partial class ResumoPedidoConversa
{
    public static string Texto(PedidoReservado reservado, DateOnly dataEntrega, string forma, string cabecalho)
    {
        ArgumentNullException.ThrowIfNull(reservado);
        var reais = CultureInfo.GetCultureInfo("pt-BR");
        var sb = new StringBuilder(cabecalho);
        foreach (var item in reservado.Itens)
        {
            // #1474: o item sai como no cardápio da vitrine, não em minúsculo como o avulso é gravado.
            sb.Append(CultureInfo.InvariantCulture, $"\n- {item.Quantidade:0.##}x {NomeCardapio.Exibicao(item.Nome)}");
            if (!string.IsNullOrWhiteSpace(item.Observacao)) sb.Append($" ({item.Observacao})");
            sb.Append(": ").Append(item.Subtotal.ToString("C", reais));
        }
        sb.Append("\nFrete: ").Append(reservado.ItemFrete.PrecoUnitario.ToString("C", reais));
        sb.Append("\nTotal: ").Append(reservado.Total.ToString("C", reais));
        sb.Append("\nEntrega: ").Append(Entrega(dataEntrega, reservado.Janela));
        // #1717-7: a comanda cobra como o console; a encomenda pede o sinal e o restante fica para a entrega.
        if (PlanoPagamentoEncomenda.TemSinal(reservado.Pedido.AgendadoParaEm, reservado.Pedido.CriadoEm))
        {
            var sinal = PlanoPagamentoEncomenda.Sinal(reservado.Total);
            // #2007 (item 5): na entrega não há link; o sinal entra antes da produção, pela forma combinada.
            if (forma == TrocarFormaPagamentoPedidoUseCase.FormaNaEntrega)
                sb.Append("\nSinal antes da produção: ").Append(sinal.ToString("C", reais)).Append(" (Pix, dinheiro ou maquininha)");
            else
                sb.Append("\nSinal agora por link: ").Append(sinal.ToString("C", reais));
            sb.Append("\nRestante na entrega: ").Append((reservado.Total - sinal).ToString("C", reais));
        }
        else
            sb.Append(forma == TrocarFormaPagamentoPedidoUseCase.FormaNaEntrega
                ? "\nPagamento: na entrega"
                : "\nPagamento: link online (Pix ou cartão)");
        return Limitar(sb.ToString());
    }

    private static readonly string[] DiasDaSemana = ["dom", "seg", "ter", "qua", "qui", "sex", "sáb"];

    /// <summary>"sex 09/10, das 12:00 às 14:00"; sem janela, só o dia (#1474).</summary>
    private static string Entrega(DateOnly data, JanelaEntrega? janela)
    {
        var dia = string.Create(CultureInfo.InvariantCulture, $"{DiasDaSemana[(int)data.DayOfWeek]} {data:dd/MM}");
        return janela is null
            ? dia
            : string.Create(CultureInfo.InvariantCulture, $"{dia}, das {janela.HoraInicio:HH:mm} às {janela.HoraFim:HH:mm}");
    }

    /// <summary>Limite do resumo em uma linha: o corpo do modelo da Meta aceita até 1024 caracteres com nome e número.</summary>
    public const int TamanhoMaximoEmUmaLinha = 900;

    /// <summary>
    /// #2871: o resumo como parâmetro do modelo <c>pedido_atualizado</c>, que sai fora da janela de 24 h. A Meta recusa
    /// parâmetro com quebra de linha ou com mais de quatro espaços seguidos: as linhas de cada parágrafo viram itens
    /// separados por ponto e vírgula e cada parágrafo termina em ponto, a não ser que já termine em pontuação ou em link.
    /// </summary>
    public static string EmUmaLinha(string texto)
    {
        ArgumentNullException.ThrowIfNull(texto);
        var linhas = texto.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var linha = Juntar(linhas);
        // Revisão da #2874: longo demais, saem os últimos itens ("- "), não o fim, onde ficam total, link e agradecimento.
        var itens = Enumerable.Range(0, linhas.Length).Where(i => linhas[i].TrimStart().StartsWith("- ", StringComparison.Ordinal)).ToArray();
        for (var omitidos = 1; linha.Length > TamanhoMaximoEmUmaLinha && omitidos < itens.Length; omitidos++)
            linha = Juntar(SemOsUltimosItens(linhas, itens[^omitidos..]));
        if (linha.Length <= TamanhoMaximoEmUmaLinha) return linha;
        var fim = TamanhoMaximoEmUmaLinha - 1;
        if (char.IsHighSurrogate(linha[fim - 1])) fim--;
        return linha[..fim].TrimEnd() + "…";
    }

    private static string Juntar(IEnumerable<string> linhas)
    {
        var paragrafos = string.Join('\n', linhas)
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParagrafoEmUmaLinha)
            .Where(p => p.Length > 0);
        return EspacosSeguidos().Replace(string.Join(' ', paragrafos), " ").Trim();
    }

    private static IEnumerable<string> SemOsUltimosItens(string[] linhas, int[] omitidos)
    {
        for (var i = 0; i < linhas.Length; i++)
        {
            if (i == omitidos[0])
                yield return string.Create(CultureInfo.InvariantCulture, $"- e mais {omitidos.Length} {(omitidos.Length == 1 ? "item" : "itens")}");
            else if (!omitidos.Contains(i))
                yield return linhas[i];
        }
    }

    private static string ParagrafoEmUmaLinha(string paragrafo)
    {
        var sb = new StringBuilder();
        foreach (var bruta in paragrafo.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var linha = bruta.StartsWith("- ", StringComparison.Ordinal) ? bruta[2..].TrimStart() : bruta;
            if (sb.Length > 0) sb.Append(sb[^1] == ':' ? " " : "; ");
            sb.Append(linha);
        }
        var texto = sb.ToString();
        if (texto.Length == 0 || ".!?…:".Contains(texto[^1], StringComparison.Ordinal)) return texto;
        var ultimaPalavra = texto[(texto.LastIndexOf(' ') + 1)..];
        return ultimaPalavra.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || ultimaPalavra.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? texto
            : texto + ".";
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex EspacosSeguidos();

    /// <summary>Corta no limite de texto da mensagem, como toda saída gravada na conversa.</summary>
    public static string Limitar(string texto) =>
        texto.Length > Mensagem.TextoTamanhoMaximo ? texto[..Mensagem.TextoTamanhoMaximo] : texto;
}

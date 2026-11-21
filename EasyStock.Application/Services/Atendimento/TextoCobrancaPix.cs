using System.Globalization;
using System.Text;
using EasyStock.Application.Ports.Output.Pdf;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// #2962 (E3): a cobrança Pix em dois balões, numa fonte só. O balão 1 acompanha o PDF do pedido; o balão 2 é SÓ o copia e
/// cola, para a cliente copiar com um toque longo, sem texto em volta.
/// </summary>
public static class TextoCobrancaPix
{
    public const string FraseDoPix = "Pague em até 30 min pelo Pix abaixo";

    private const int MaximoDeItensNaLegenda = 5;

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Balão 1: código e loja, itens, total (ou sinal), entrega e a frase do Pix.</summary>
    public static string Legenda(ResumoPedidoDto resumo, PixPdfDto pix)
    {
        ArgumentNullException.ThrowIfNull(resumo);
        ArgumentNullException.ThrowIfNull(pix);
        var sb = new StringBuilder($"Pedido {resumo.Codigo}");
        if (!string.IsNullOrWhiteSpace(resumo.NomeLoja)) sb.Append(" · ").Append(resumo.NomeLoja);

        foreach (var item in resumo.Itens.Take(MaximoDeItensNaLegenda))
            sb.Append('\n').Append(item.Quantidade.ToString("0.##", PtBr)).Append("× ").Append(item.Nome)
                .Append(" · ").Append(item.Subtotal.ToString("C", PtBr));
        if (resumo.Itens.Count > MaximoDeItensNaLegenda)
        {
            var restantes = resumo.Itens.Count - MaximoDeItensNaLegenda;
            sb.Append("\ne mais ").Append(restantes.ToString(PtBr)).Append(restantes == 1 ? " item" : " itens");
        }

        sb.Append("\nTotal ").Append(resumo.Total.ToString("C", PtBr));
        if (pix.RestanteNaEntrega > 0m)
            sb.Append("\nSinal de ").Append(pix.ValorDoPix.ToString("C", PtBr))
                .Append(" agora; restante na entrega: ").Append(pix.RestanteNaEntrega.ToString("C", PtBr));
        if (!string.IsNullOrWhiteSpace(resumo.Entrega)) sb.Append("\nEntrega ").Append(resumo.Entrega);
        sb.Append('\n').Append(FraseDoPix);
        return sb.ToString();
    }

    /// <summary>Balão 2: exatamente o código, sem nenhuma palavra em volta.</summary>
    public static string BalaoDoCodigo(PixPdfDto pix)
    {
        ArgumentNullException.ThrowIfNull(pix);
        return pix.CopiaCola.Trim();
    }
}

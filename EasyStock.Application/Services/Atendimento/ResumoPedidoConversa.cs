using System.Globalization;
using System.Text;
using EasyStock.Application.Services.Storefront;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// Texto do pedido da conversa: itens, frete, total, entrega e forma de pagamento. Serve à nota do
/// cardápio do site (S48) e ao resumo que a operadora manda ao cliente pelo console (F03).
/// </summary>
public static class ResumoPedidoConversa
{
    public static string Texto(PedidoReservado reservado, DateOnly dataEntrega, string forma, string cabecalho)
    {
        ArgumentNullException.ThrowIfNull(reservado);
        var reais = CultureInfo.GetCultureInfo("pt-BR");
        var sb = new StringBuilder(cabecalho);
        foreach (var item in reservado.Itens)
        {
            sb.Append(CultureInfo.InvariantCulture, $"\n- {item.Quantidade:0.##}x {item.Nome}");
            if (!string.IsNullOrWhiteSpace(item.Observacao)) sb.Append($" ({item.Observacao})");
            sb.Append(": ").Append(item.Subtotal.ToString("C", reais));
        }
        sb.Append("\nFrete: ").Append(reservado.ItemFrete.PrecoUnitario.ToString("C", reais));
        sb.Append("\nTotal: ").Append(reservado.Total.ToString("C", reais));
        sb.Append("\nEntrega: ").Append(dataEntrega.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));
        sb.Append(forma == TrocarFormaPagamentoPedidoUseCase.FormaNaEntrega
            ? "\nPagamento: na entrega"
            : "\nPagamento: link online (Pix ou cartão)");
        return Limitar(sb.ToString());
    }

    /// <summary>Corta no limite de texto da mensagem, como toda saída gravada na conversa.</summary>
    public static string Limitar(string texto) =>
        texto.Length > Mensagem.TextoTamanhoMaximo ? texto[..Mensagem.TextoTamanhoMaximo] : texto;
}

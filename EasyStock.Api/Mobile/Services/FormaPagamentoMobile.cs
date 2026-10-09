namespace EasyStock.Api.Mobile.Services;

/// <summary>
/// #1493 — forma de pagamento que o PWA manda no pedido entregue e no lancamento de caixa.
/// Mesma lista do <c>RegistrarPagamentoPedidoUseCase</c> e do <c>MovimentoCaixa.Metodo</c>.
/// </summary>
public static class FormaPagamentoMobile
{
    /// <summary>Forma usada quando o aparelho nao informa (PWA antigo, antes do #1493).</summary>
    public const string Padrao = "dinheiro";

    private static readonly HashSet<string> Validas = new(StringComparer.Ordinal)
    {
        "pix", "dinheiro", "credito", "debito", "transferencia", "outro"
    };

    /// <summary>
    /// Vazio vira null (aparelho antigo). Valor fora da lista vira "outro": se o aparelho
    /// mandou algo, nao e certeza de dinheiro, entao nao pode cair na gaveta.
    /// </summary>
    public static string? Normalizar(string? metodo)
    {
        if (string.IsNullOrWhiteSpace(metodo)) return null;
        var m = metodo.Trim().ToLowerInvariant();
        return Validas.Contains(m) ? m : "outro";
    }

    /// <summary>Forma que vai para o ERP: a informada ou <see cref="Padrao"/>.</summary>
    public static string ParaErp(string? metodo) => Normalizar(metodo) ?? Padrao;
}

namespace EasyStock.Domain.Financeiro;

/// <summary>
/// Formas de pagamento que o Caixa conhece (#2095, F8): a mesma lista do console (<c>METODOS_CAIXA</c> em
/// <c>EasyStock.Console/src/dominio/caixa.js</c>), do pagamento de pedido e da venda. Pagamento de parcela gera
/// movimento no caixa; método fora desta lista ("bitcoin") some da gaveta e dos totais por forma.
/// </summary>
public static class MetodosPagamentoCaixa
{
    public static readonly IReadOnlyList<string> Todos = ["pix", "dinheiro", "credito", "debito", "transferencia", "outro"];

    /// <summary>Forma normalizada (minúscula, sem espaço nas pontas) ou null quando não é do caixa.</summary>
    public static string? Normalizar(string? metodo)
    {
        if (string.IsNullOrWhiteSpace(metodo)) return null;
        var m = metodo.Trim().ToLowerInvariant();
        return Todos.Contains(m) ? m : null;
    }
}

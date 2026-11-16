namespace EasyStock.Web.Helpers;

/// <summary>Parcela planejada de uma conta a pagar ou a receber (corpo enviado a API).</summary>
public sealed record ParcelaPlanejada(int Numero, decimal Valor, DateTime DataVencimento, string? MetodoPlanejado);

/// <summary>Monta as parcelas de uma conta a pagar ou a receber.</summary>
public static class ParcelasHelper
{
    public static List<ParcelaPlanejada> Montar(decimal valorTotal, int n, DateTime primeira, string intervalo)
    {
        var lista = new List<ParcelaPlanejada>(n);
        var valorParcela = Math.Round(valorTotal / n, 2, MidpointRounding.AwayFromZero);
        var residuo = valorTotal - valorParcela * n;
        for (var i = 1; i <= n; i++)
        {
            var v = i == n ? valorParcela + residuo : valorParcela;
            // Sempre a partir da primeira data: encadear AddMonths(1) derruba 31/01 para dia 28 de la em diante.
            var vencimento = intervalo switch
            {
                "quinzenal" => primeira.AddDays(15 * (i - 1)),
                "semanal" => primeira.AddDays(7 * (i - 1)),
                _ => primeira.AddMonths(i - 1)
            };
            lista.Add(new ParcelaPlanejada(i, v, vencimento, null));
        }
        return lista;
    }
}

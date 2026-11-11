using EasyStock.Application.UseCases.Caixa;

namespace EasyStock.Application.UseCases.Financeiro.Dashboard;

/// <summary>
/// #2268: o dinheiro que o Caixa soma fora das contas (pagamento de pedido, inclusive o Pix do Mercado Pago, venda,
/// estorno de venda e movimento avulso). Mesmas linhas do Caixa (<see cref="ICaixaSaldoCalculator.GetLinhasExtrasAsync"/>)
/// e do Extrato. Pagamento de pedido não dá baixa em conta a receber, então nada aqui já está nas contas; o movimento
/// que um pagamento de parcela referencia fica de fora.
/// </summary>
public class RealizadoDoCaixa(ICaixaSaldoCalculator calc, IFluxoCaixaQueries queries)
{
    public async Task<IReadOnlyList<RealizadoNoCaixaDto>> ListarAsync(
        Guid empresaId, DateTime iniUtc, DateTime fimUtc, CancellationToken ct = default)
    {
        var linhas = await calc.GetLinhasExtrasAsync(empresaId, iniUtc, fimUtc);
        var avulsos = await queries.MovimentosAvulsosDoCaixaAsync(empresaId, iniUtc, fimUtc, ct);
        return [.. linhas.Select(l => new RealizadoNoCaixaDto(l.Hora, l.Valor)), .. avulsos];
    }

    /// <summary>Entradas e saídas (valor positivo) em [iniUtc, fimUtc).</summary>
    public static (decimal Entrou, decimal Saiu) Somar(IEnumerable<RealizadoNoCaixaDto> linhas, DateTime iniUtc, DateTime fimUtc)
    {
        decimal entrou = 0m, saiu = 0m;
        foreach (var l in linhas)
        {
            if (l.Em < iniUtc || l.Em >= fimUtc) continue;
            if (l.Valor >= 0) entrou += l.Valor; else saiu -= l.Valor;
        }
        return (entrou, saiu);
    }
}

using EasyStock.Application.UseCases.Caixa;

namespace EasyStock.Application.UseCases.ListarLinhasCaixa;

/// <param name="Desde">Início do período em UTC, inclusivo.</param>
/// <param name="Ate">Fim do período em UTC, inclusivo, como devolve <c>DataUtc.FimDoPeriodoUtc</c>.</param>
public sealed record ListarLinhasCaixaQuery(Guid EmpresaId, DateTime Desde, DateTime Ate);

/// <summary>
/// #2262: as linhas que o Caixa soma fora dos movimentos (pagamento de pedido, venda e estorno de venda) num
/// período. O recebimento de pedido, inclusive o confirmado pelo webhook do Mercado Pago, não grava movimento de
/// caixa (#926): sem esta leitura, o Extrato do Financeiro mostrava a devolução sem o recebimento que ela devolve.
/// Mesma fonte do <c>GET api/caixa/dia</c>. O <c>/dia</c> corta no fechamento do caixa; aqui vale o período inteiro,
/// então o recebido depois do fechamento entra no Extrato e não no dia fechado.
/// </summary>
public class ListarLinhasCaixaUseCase(ICaixaSaldoCalculator calc)
{
    /// <summary>A lista não é paginada; o teto protege a consulta.</summary>
    public const int DiasNoMaximo = 366;

    public Task<IReadOnlyList<CaixaLinhaExtraResult>> ExecuteAsync(ListarLinhasCaixaQuery q)
    {
        UseCaseGuards.EnsureEmpresaId(q.EmpresaId);
        if (q.Ate < q.Desde) throw new UseCaseValidationException("O fim do período vem antes do início.");
        if (q.Ate - q.Desde > TimeSpan.FromDays(DiasNoMaximo))
            throw new UseCaseValidationException($"Consulte no máximo {DiasNoMaximo} dias por vez.");
        // As linhas usam fim exclusivo e o período chega com fim inclusivo: 1 µs antes do dia seguinte para data pura,
        // o milissegundo .999 quando vem com fuso (Console). Nesse caso sobra menos de 1 ms, como em /movimentos.
        return calc.GetLinhasExtrasAsync(q.EmpresaId, q.Desde, FimExclusivo(q.Ate));
    }

    private static DateTime FimExclusivo(DateTime ate) =>
        ate.Ticks > DateTime.MaxValue.Ticks - 10 ? DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc) : ate.AddTicks(10);
}

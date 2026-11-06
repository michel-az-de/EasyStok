using EasyStock.Application.UseCases.Caixa;

namespace EasyStock.Application.UseCases.ListarLinhasCaixa;

/// <param name="Desde">Início do período em UTC, inclusivo.</param>
/// <param name="Ate">Fim do período em UTC, inclusivo, como devolve <c>DataUtc.FimDoPeriodoUtc</c>.</param>
public sealed record ListarLinhasCaixaQuery(Guid EmpresaId, DateTime Desde, DateTime Ate);

/// <summary>
/// #2262: as linhas que o Caixa soma fora dos movimentos (pagamento de pedido, venda e estorno de venda) num
/// período. O recebimento de pedido, inclusive o confirmado pelo webhook do Mercado Pago, não grava movimento de
/// caixa (#926): sem esta leitura, o Extrato do Financeiro mostrava a devolução sem o recebimento que ela devolve.
/// Mesma fonte do <c>GET api/caixa/dia</c>, para as somas das duas telas baterem.
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
        // As linhas usam fim exclusivo; o período chega com fim inclusivo (23:59:59.999999 do último dia).
        return calc.GetLinhasExtrasAsync(q.EmpresaId, q.Desde, q.Ate.AddTicks(10));
    }
}

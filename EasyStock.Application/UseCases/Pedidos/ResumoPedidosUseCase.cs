namespace EasyStock.Application.UseCases.Pedidos;

/// <summary>
/// #3294: os indicadores da tela Pedidos de Cadastros. O resumo usa os filtros da lista, menos a situação (os
/// indicadores filtram por ela) e a página. Com "feito desde", compara com o mesmo trecho do período anterior:
/// de <c>desde - N dias</c> até <c>agora - N dias</c>, N = dias do período contando hoje, para "hoje" valer
/// ontem até esta hora. Com filtro de entrega ou data final, não há comparação.
/// </summary>
public sealed record ResumoPedidosQuery(
    Guid EmpresaId,
    Guid? ClienteId = null,
    DateTime? Desde = null,
    DateTime? Ate = null,
    string? Search = null,
    DateTime? EntregaDesde = null,
    DateTime? EntregaAte = null);

public sealed record ResumoPedidosResult(
    int Pedidos,
    int Vendas,
    decimal Vendido,
    decimal TicketMedio,
    ValorEPedidos AReceber,
    int AguardandoPagamento,
    int AguardandoAprovacao,
    int EmAndamento,
    int Atrasados,
    ValorEPedidos Cancelados,
    int PrimeiraCompra,
    int Recompra,
    IReadOnlyList<ItemMaisVendido> MaisVendidos,
    PeriodoAnteriorResult? Anterior);

public sealed record ValorEPedidos(decimal Valor, int Pedidos);

public sealed record PeriodoAnteriorResult(DateTime Desde, DateTime Ate, int Pedidos, decimal Vendido);

public sealed class ResumoPedidosUseCase(IPedidosResumoQuery consulta, TimeProvider relogio)
{
    public async Task<ResumoPedidosResult> ExecuteAsync(ResumoPedidosQuery q, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(q.EmpresaId);
        var agora = relogio.GetUtcNow().UtcDateTime;
        var filtro = new FiltroDosPedidos(q.EmpresaId, q.ClienteId, q.Desde, q.Ate, q.Search, q.EntregaDesde, q.EntregaAte);
        var r = await consulta.ResumirAsync(filtro, agora, ct);

        PeriodoAnteriorResult? anterior = null;
        if (JanelaAnterior(q, agora) is var (de, ate))
        {
            var v = await consulta.VendasAsync(filtro with { Desde = de, Ate = ate }, ct);
            anterior = new PeriodoAnteriorResult(de, ate, v.Pedidos, v.Vendido);
        }

        return new ResumoPedidosResult(
            r.Pedidos, r.Vendas, r.Vendido,
            r.Vendas == 0 ? 0m : Math.Round(r.Vendido / r.Vendas, 2, MidpointRounding.AwayFromZero),
            new ValorEPedidos(r.AReceberValor, r.AReceberPedidos),
            r.AguardandoPagamento, r.AguardandoAprovacao, r.EmAndamento, r.Atrasados,
            new ValorEPedidos(r.CanceladosValor, r.CanceladosPedidos),
            r.PrimeiraCompra, r.Recompra, r.MaisVendidos, anterior);
    }

    /// <summary>O mesmo trecho do período anterior, ou nulo quando a comparação não tem sentido.</summary>
    public static (DateTime De, DateTime Ate)? JanelaAnterior(ResumoPedidosQuery q, DateTime agoraUtc)
    {
        if (q.Desde is not { } desde || q.Ate is not null || q.EntregaDesde is not null || q.EntregaAte is not null) return null;
        if (desde >= agoraUtc) return null;
        var dias = HorarioBrasil.DataOperacional(agoraUtc).DayNumber - HorarioBrasil.DataOperacional(desde).DayNumber + 1;
        return (desde.AddDays(-dias), agoraUtc.AddDays(-dias));
    }
}

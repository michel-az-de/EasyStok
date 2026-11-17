namespace EasyStock.Api.Controllers;

/// <summary>
/// Resposta de <c>GET api/movimentacoes/kpis</c>. Unidades e contagens são de toda a equipe; a receita é relatório e
/// sai nula para quem não tem a permissão de relatórios (#2821).
/// </summary>
public sealed record KpisMovimentacaoResponse(decimal TotalUnidades, decimal? ReceitaTotal, int TotalVendas, int TotalPerdas)
{
    public static KpisMovimentacaoResponse De(KpisMovimentacao k, bool podeVerReceita) =>
        new(k.TotalUnidades, podeVerReceita ? k.ReceitaTotal : null, k.TotalVendas, k.TotalPerdas);
}

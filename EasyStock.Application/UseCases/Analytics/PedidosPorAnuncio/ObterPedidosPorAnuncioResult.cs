namespace EasyStock.Application.UseCases.Analytics.PedidosPorAnuncio;

public sealed record ObterPedidosPorAnuncioResult(
    string Plataforma,
    string Campanha,
    int TotalPedidos,
    decimal ValorTotal,
    decimal TicketMedio)
{
    public const string PlataformaSemOrigem = "sem_origem";
    public const string RotuloSemOrigem = "Sem origem de anúncio";
    public const string SemCampanha = "Sem campanha";

    public static ObterPedidosPorAnuncioResult FromDto(PedidoPorAnuncio dto)
    {
        var semOrigem = dto.Plataforma is null;
        return new ObterPedidosPorAnuncioResult(
            semOrigem ? PlataformaSemOrigem : dto.Plataforma!,
            semOrigem ? RotuloSemOrigem : dto.Campanha ?? SemCampanha,
            dto.TotalPedidos,
            Math.Round(dto.ValorTotal, 2),
            dto.TotalPedidos > 0 ? Math.Round(dto.ValorTotal / dto.TotalPedidos, 2) : 0m);
    }
}

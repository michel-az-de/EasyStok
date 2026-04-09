namespace EasyStock.Web.Models.Api;

public record ReceitaPorPeriodoApi
{
    public int Ano { get; init; }
    public int Mes { get; init; }
    public decimal ReceitaBruta { get; init; }
    public int TotalVendas { get; init; }
    public int TotalItensVendidos { get; init; }
    public decimal TicketMedio { get; init; }
}

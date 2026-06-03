namespace EasyStock.Web.Models.Api;

public record ResumoDiaApi
{
    public int PedidosEntreguesHoje { get; init; }
    public decimal FaturamentoHoje { get; init; }
    public decimal TicketMedioHoje { get; init; }
    public int PedidosPendentes { get; init; }
    public decimal ValorPedidosPendentes { get; init; }
    public bool CaixaAbertaHoje { get; init; }
    public bool CaixaFechadaHoje { get; init; }
    public decimal SaldoCaixaAtual { get; init; }
    public int PixRecebidosHoje { get; init; }
    public decimal ValorPixHoje { get; init; }
    public bool OnboardingCompleto { get; init; }
    public int CategoriasCount { get; init; }
    public int EntradasCount { get; init; }
}

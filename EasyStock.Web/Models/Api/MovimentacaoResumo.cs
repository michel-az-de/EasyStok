namespace EasyStock.Web.Models.Api;

public record MovimentacaoResumo
{
    public int Ano { get; init; }
    public int Mes { get; init; }
    public int Dia { get; init; }
    public string Tipo { get; init; } = string.Empty;
    public int TotalMovimentacoes { get; init; }
    public int QuantidadeTotal { get; init; }
    public decimal ValorTotal { get; init; }
}

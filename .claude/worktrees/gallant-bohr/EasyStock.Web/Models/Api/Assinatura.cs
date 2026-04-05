namespace EasyStock.Web.Models.Api;

public record Assinatura
{
    public required string Plano { get; init; }
    public decimal Preco { get; init; }
    public DateOnly ProxCobranca { get; init; }
    public required string Status { get; init; }
    public UsoPlaon Uso { get; init; } = new();
}

public record UsoPlaon(
    int LojasAtual = 0, int LojasMax = 0,
    int UsuariosAtual = 0, int UsuariosMax = 0,
    int ProdutosAtual = 0, int ProdutosMax = 0,
    int IaAtual = 0, int IaMax = 0);

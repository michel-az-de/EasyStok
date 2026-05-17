namespace EasyStock.Web.Models.Api;

/// <summary>Resposta de /api/estoque/contadores. Cadastrados inclui lotes com qty = 0.</summary>
public record EstoqueContadores
{
    public int Cadastrados { get; init; }
    public int ComSaldo { get; init; }
}

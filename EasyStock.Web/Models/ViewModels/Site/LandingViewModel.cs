namespace EasyStock.Web.Models.ViewModels.Site;

/// <summary>
/// Dados disponiveis na landing principal. Vazio por enquanto —
/// futuramente pode trazer numeros publicos (clientes, depoimentos, etc).
/// </summary>
public sealed class LandingViewModel
{
    public bool VagasFounderAbertas { get; init; } = true;
    public int VagasFounderRestantes { get; init; } = 7;
}

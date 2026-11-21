namespace EasyStock.Api.Mobile.Services;

/// <summary>
/// #409: canal que o PWA marca no pedido (por onde a cliente pediu). A lista de opções mora na tela;
/// o servidor só aceita o formato de código curto, para a lista poder crescer sem nova versão da API.
/// </summary>
public static class CanalPedidoMobile
{
    public const int TamanhoMaximo = 20;

    /// <summary>Vazio ou fora do formato vira null (não informado). Caixa e espaços são normalizados.</summary>
    public static string? Normalizar(string? canal)
    {
        if (string.IsNullOrWhiteSpace(canal)) return null;
        var c = canal.Trim().ToLowerInvariant();
        return c.Length <= TamanhoMaximo && c.All(ch => ch is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_')
            ? c
            : null;
    }
}

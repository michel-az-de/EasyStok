namespace EasyStock.Application.UseCases.Atendimento.Entregas;

/// <summary>
/// Link de rota do Google Maps (S44) montado só com os endereços, na ordem das paradas, sem chave de
/// API: <c>https://www.google.com/maps/dir/{endereço 1}/{endereço 2}/...</c>. Endereço vazio é pulado.
/// </summary>
public static class RotaMaps
{
    public const string Base = "https://www.google.com/maps/dir/";

    public static string? Montar(IEnumerable<string?> enderecos)
    {
        var partes = enderecos
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => Uri.EscapeDataString(e!.Trim()))
            .ToList();
        return partes.Count == 0 ? null : Base + string.Join('/', partes);
    }
}

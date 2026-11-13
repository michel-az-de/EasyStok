using EasyStock.Domain.ValueObjects;

namespace EasyStock.Api.Mobile.Services.Linkers;

/// <summary>
/// Chave para comparar o mesmo telefone em grafias diferentes: só os dígitos
/// (<see cref="TelefoneE164.SoDigitos"/>), sem o DDI 55 quando há 12 ou 13 dígitos. Vazia = sem telefone.
/// </summary>
internal static class ChaveDeTelefone
{
    public static string De(string? telefone)
    {
        var d = TelefoneE164.SoDigitos(telefone);
        return d.Length is 12 or 13 && d.StartsWith("55", StringComparison.Ordinal) ? d[2..] : d;
    }
}

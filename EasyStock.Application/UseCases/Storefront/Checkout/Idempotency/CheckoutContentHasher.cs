using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EasyStock.Application.UseCases.Storefront.Checkout.Idempotency;

/// <summary>
/// Calcula o hash canônico do payload de checkout para uso como
/// <c>ContentHash</c> na idempotência (TASK-EZ-CHECKOUT-002).
///
/// <para>
/// Algoritmo: SHA-256 do JSON normalizado — items ordenados por
/// <c>CardapioItemId</c>, CEP sem máscara, campos determinísticos.
/// </para>
/// </summary>
public static class CheckoutContentHasher
{
    /// <summary>
    /// Computa SHA-256 hex lowercase (64 chars) do payload normalizado.
    /// A mesma entrada sempre gera o mesmo hash independente da ordem dos itens.
    /// </summary>
    public static string ComputarHash(IniciarCheckoutInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var payload = JsonSerializer.Serialize(new
        {
            input.Slug,
            Items = (input.Items ?? Array.Empty<CheckoutItemInput>())
                .Select(i => new { i.CardapioItemId, i.Qtd })
                .OrderBy(i => i.CardapioItemId),
            input.JanelaId,
            DataEntrega = input.DataEntrega.ToString("yyyy-MM-dd"),
            Cep = NormalizarCep(input.Cep),
        });

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string NormalizarCep(string? cep)
    {
        if (string.IsNullOrWhiteSpace(cep)) return string.Empty;
        var sb = new StringBuilder(cep.Length);
        foreach (var c in cep)
            if (char.IsDigit(c)) sb.Append(c);
        return sb.ToString();
    }
}

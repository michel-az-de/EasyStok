using System.Net.Mail;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyStock.Domain.ValueObjects;

/// <summary>
/// Endereço de e-mail validado. Armazena o valor normalizado (lowercase, trimmed).
/// Conversão implícita para string facilita uso em DTOs e comparações LINQ.
/// </summary>
[JsonConverter(typeof(EmailAddressJsonConverter))]
public sealed record EmailAddress
{
    public string Value { get; }

    private EmailAddress(string value)
    {
        Value = value.Trim().ToLowerInvariant();
    }

    /// <summary>Cria um <see cref="EmailAddress"/> validado ou lança <see cref="ArgumentException"/>.</summary>
    public static EmailAddress From(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("E-mail não pode ser vazio.", nameof(email));

        email = email.Trim();

        // RFC-5322 validation via MailAddress
        try
        {
            var addr = new MailAddress(email);
            // Extra checks: must have a dot in domain and domain length >= 3
            var host = addr.Host;
            if (!host.Contains('.') || host.Length < 3)
                throw new FormatException();
        }
        catch
        {
            throw new ArgumentException($"E-mail '{email}' possui formato inválido.", nameof(email));
        }

        return new EmailAddress(email);
    }

    /// <summary>Tenta criar sem lançar exceção. Retorna null se inválido.</summary>
    public static EmailAddress? TryFrom(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        try { return From(email); }
        catch { return null; }
    }

    /// <summary>Conversão implícita para string — facilita uso em LINQ e comparações.</summary>
    public static implicit operator string(EmailAddress e) => e.Value;

    public override string ToString() => Value;

    private sealed class EmailAddressJsonConverter : JsonConverter<EmailAddress>
    {
        public override EmailAddress? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            var raw = reader.GetString();
            return raw is null ? null : TryFrom(raw);
        }

        public override void Write(Utf8JsonWriter writer, EmailAddress value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.Value);
    }
}

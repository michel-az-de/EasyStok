using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace EasyStock.Domain.ValueObjects;

/// <summary>
/// Número de telefone normalizado (apenas dígitos, opcional DDD).
/// Aceita formatos brasileiros e internacionais simples.
/// </summary>
[JsonConverter(typeof(TelefoneJsonConverter))]
public sealed record Telefone
{
    private static readonly Regex ValidDigits = new(@"^\+?\d{7,15}$", RegexOptions.Compiled);

    public string Value { get; }

    private Telefone(string value) => Value = value;

    /// <summary>Cria um <see cref="Telefone"/> validado a partir de um string de entrada.</summary>
    public static Telefone From(string telefone)
    {
        if (string.IsNullOrWhiteSpace(telefone))
            throw new ArgumentException("Telefone não pode ser vazio.", nameof(telefone));

        // Normalize: remove spaces, parentheses, dashes; keep leading '+'
        var normalized = telefone.Trim();
        var prefix = normalized.StartsWith('+') ? "+" : "";
        var digits = prefix + Regex.Replace(normalized.TrimStart('+'), @"[\s\-().\/]", "");

        if (!ValidDigits.IsMatch(digits))
            throw new ArgumentException(
                $"Telefone '{telefone}' inválido. Informe entre 7 e 15 dígitos.",
                nameof(telefone));

        return new Telefone(digits);
    }

    public static Telefone? TryFrom(string? telefone)
    {
        if (string.IsNullOrWhiteSpace(telefone)) return null;
        try { return From(telefone); }
        catch { return null; }
    }

    public static implicit operator string(Telefone t) => t.Value;

    public override string ToString() => Value;

    private sealed class TelefoneJsonConverter : JsonConverter<Telefone>
    {
        public override Telefone? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            var raw = reader.GetString();
            return raw is null ? null : TryFrom(raw);
        }

        public override void Write(Utf8JsonWriter writer, Telefone value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.Value);
    }
}

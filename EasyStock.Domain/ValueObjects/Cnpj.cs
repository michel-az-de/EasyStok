using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace EasyStock.Domain.ValueObjects;

/// <summary>
/// CNPJ ou CPF normalizado (apenas dígitos). Valida o formato sem aplicar o
/// algoritmo de dígito verificador, pois o campo 'Documento' do Fornecedor
/// aceita ambos os tipos de documento.
/// </summary>
[JsonConverter(typeof(CnpjJsonConverter))]
public sealed record Cnpj
{
    // Aceita 11 dígitos (CPF) ou 14 dígitos (CNPJ)
    private static readonly Regex DigitsOnly = new(@"^\d{11}(\d{3})?$", RegexOptions.Compiled);

    public string Value { get; }

    private Cnpj(string value) => Value = value;

    /// <summary>Cria um <see cref="Cnpj"/> com apenas os dígitos do documento informado.</summary>
    public static Cnpj From(string documento)
    {
        if (string.IsNullOrWhiteSpace(documento))
            throw new ArgumentException("Documento não pode ser vazio.", nameof(documento));

        // Strip formatting characters (dots, slashes, dashes)
        var digits = Regex.Replace(documento.Trim(), @"[.\-/]", "");

        if (!DigitsOnly.IsMatch(digits))
            throw new ArgumentException(
                $"Documento '{documento}' inválido. Informe um CPF (11 dígitos) ou CNPJ (14 dígitos).",
                nameof(documento));

        return new Cnpj(digits);
    }

    public static Cnpj? TryFrom(string? documento)
    {
        if (string.IsNullOrWhiteSpace(documento)) return null;
        try { return From(documento); }
        catch { return null; }
    }

    /// <summary>Formata o documento para exibição (CPF: 000.000.000-00 / CNPJ: 00.000.000/0000-00).</summary>
    public string Formatado() => Value.Length == 11
        ? $"{Value[..3]}.{Value[3..6]}.{Value[6..9]}-{Value[9..]}"
        : $"{Value[..2]}.{Value[2..5]}.{Value[5..8]}/{Value[8..12]}-{Value[12..]}";

    public static implicit operator string(Cnpj c) => c.Value;

    public override string ToString() => Value;

    private sealed class CnpjJsonConverter : JsonConverter<Cnpj>
    {
        public override Cnpj? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            var raw = reader.GetString();
            return raw is null ? null : TryFrom(raw);
        }

        public override void Write(Utf8JsonWriter writer, Cnpj value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.Value);
    }
}

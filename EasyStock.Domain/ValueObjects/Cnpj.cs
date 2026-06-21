using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace EasyStock.Domain.ValueObjects;

/// <summary>
/// CNPJ ou CPF normalizado (apenas dígitos). <see cref="From"/>/<see cref="TryFrom"/> validam
/// apenas o formato (11 ou 14 dígitos) e servem para normalização, pois o campo 'Documento'
/// aceita ambos os tipos. Para validar o dígito verificador de um CNPJ (14 dígitos) use
/// <see cref="EhValido"/> — espelha <c>Cpf.EhValido</c>.
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

    /// <summary>True quando o documento, só dígitos, tem 14 caracteres (forma de CNPJ).</summary>
    public static bool TemFormaDeCnpj(string? documento) => SomenteDigitos(documento).Length == 14;

    /// <summary>
    /// True se o documento for um CNPJ válido: 14 dígitos, não todos iguais, e ambos os
    /// dígitos verificadores (módulo 11) conferem. Aceita entrada com ou sem máscara.
    /// Sequências repetidas (ex.: "11111111111111") passam na conta mas são rejeitadas.
    /// </summary>
    public static bool EhValido(string? documento)
    {
        var d = SomenteDigitos(documento);
        if (d.Length != 14) return false;
        if (TodosIguais(d)) return false;

        return d[12] == DigitoVerificador(d, 12)
            && d[13] == DigitoVerificador(d, 13);
    }

    /// <summary>Formata o documento para exibição (CPF: 000.000.000-00 / CNPJ: 00.000.000/0000-00).</summary>
    public string Formatado() => Value.Length == 11
        ? $"{Value[..3]}.{Value[3..6]}.{Value[6..9]}-{Value[9..]}"
        : $"{Value[..2]}.{Value[2..5]}.{Value[5..8]}/{Value[8..12]}-{Value[12..]}";

    public static implicit operator string(Cnpj c) => c.Value;

    public override string ToString() => Value;

    private static string SomenteDigitos(string? s) =>
        string.IsNullOrEmpty(s) ? string.Empty : Regex.Replace(s, @"\D", "");

    private static bool TodosIguais(string d)
    {
        for (var i = 1; i < d.Length; i++)
            if (d[i] != d[0]) return false;
        return true;
    }

    /// <summary>
    /// Dígito verificador (módulo 11) do CNPJ na <paramref name="posicao"/> 12 (1º DV) ou
    /// 13 (2º DV). Pesos cíclicos 2..9 da direita para a esquerda. Resto &lt; 2 → 0.
    /// </summary>
    private static char DigitoVerificador(string d, int posicao)
    {
        var soma = 0;
        var peso = 2;
        for (var i = posicao - 1; i >= 0; i--)
        {
            soma += (d[i] - '0') * peso;
            peso = peso == 9 ? 2 : peso + 1;
        }
        var resto = soma % 11;
        var dv = resto < 2 ? 0 : 11 - resto;
        return (char)('0' + dv);
    }

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

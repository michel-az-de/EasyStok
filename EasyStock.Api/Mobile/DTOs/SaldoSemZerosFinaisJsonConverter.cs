using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyStock.Api.Mobile.DTOs;

/// <summary>
/// #2718: o saldo vem do Postgres em <c>numeric(18,3)</c>, com escala 3 (1.500, 7.000). O aparelho
/// recebia inteiro; escrever "7.000" mudaria o contrato. Grava sem zeros finais: 7 sai 7 e 1,5 sai 1.5.
/// A leitura aceita inteiro (aparelho antigo) e fracionado.
/// </summary>
public sealed class SaldoSemZerosFinaisJsonConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetDecimal();

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(SemZerosFinais(value));

    /// <summary>Mesmo valor, sem os zeros à direita da escala (1.500 vira 1.5; 7.000 vira 7).</summary>
    public static decimal SemZerosFinais(decimal valor) => valor / 1.000000000000000000000000000000000m;
}

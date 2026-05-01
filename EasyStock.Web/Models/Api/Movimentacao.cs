using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyStock.Web.Models.Api;

public record Movimentacao
{
    public required string Id { get; init; }
    public required string ProdutoId { get; init; }
    public string? ProdutoVariacaoId { get; init; }
    public string? VendaId { get; init; }
    public string Tipo { get; init; } = string.Empty;
    public string Natureza { get; init; } = string.Empty;
    public QuantidadeDto? Quantidade { get; init; }
    public DinheiroDto? ValorUnitario { get; init; }
    public DinheiroDto? ValorTotal { get; init; }
    public DateTime DataMovimentacao { get; init; }
    public string? Descricao { get; init; }
    public string? DocumentoReferencia { get; init; }
    public DateTime? EstornadaEm { get; init; }
    public string? MovimentacaoEstornadaId { get; init; }
    public Produto? Produto { get; init; }
    public Variacao? ProdutoVariacao { get; init; }

    // Auditoria de movimentacao (P0-2): quem/de onde/qual dispositivo.
    public string? UsuarioId { get; init; }
    public string? Ip { get; init; }
    public string? UserAgent { get; init; }
    public string? DispositivoId { get; init; }
    public string? MotivoEstorno { get; init; }

    // Computed view helpers
    public int Qty => Quantidade?.Value ?? 0;
    public decimal? Custo => ValorUnitario?.Valor;
    public DateOnly Data => DateOnly.FromDateTime(DataMovimentacao);

    // Resumo curto pra exibir na UI: "Chrome 120 / Mac" -> apenas "Chrome 120 / Mac" abreviado.
    public string? UserAgentResumo => string.IsNullOrWhiteSpace(UserAgent)
        ? null
        : UserAgent.Length > 60 ? UserAgent[..60] + "…" : UserAgent;
}

[JsonConverter(typeof(QuantidadeDtoJsonConverter))]
public record QuantidadeDto
{
    public int Value { get; init; }
}

[JsonConverter(typeof(DinheiroDtoJsonConverter))]
public record DinheiroDto
{
    public decimal Valor { get; init; }
}

public record KpisResponse
{
    public int TotalUnidades { get; init; }
    public decimal ReceitaTotal { get; init; }
    public int TotalVendas { get; init; }
    public int TotalPerdas { get; init; }
}

// ── JsonConverters: handle both primitive (new) and object (old) JSON formats ──

internal sealed class QuantidadeDtoJsonConverter : JsonConverter<QuantidadeDto>
{
    public override QuantidadeDto? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType == JsonTokenType.Number)
            return new QuantidadeDto { Value = reader.GetInt32() };

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            int val = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType == JsonTokenType.PropertyName &&
                    reader.GetString()!.Equals("value", StringComparison.OrdinalIgnoreCase))
                {
                    reader.Read();
                    val = reader.GetInt32();
                }
            }
            return new QuantidadeDto { Value = val };
        }

        throw new JsonException($"Unexpected token {reader.TokenType} for QuantidadeDto.");
    }

    public override void Write(Utf8JsonWriter writer, QuantidadeDto value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value.Value);
}

internal sealed class DinheiroDtoJsonConverter : JsonConverter<DinheiroDto>
{
    public override DinheiroDto? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType == JsonTokenType.Number)
            return new DinheiroDto { Valor = reader.GetDecimal() };

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            decimal val = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType == JsonTokenType.PropertyName &&
                    reader.GetString()!.Equals("valor", StringComparison.OrdinalIgnoreCase))
                {
                    reader.Read();
                    val = reader.GetDecimal();
                }
            }
            return new DinheiroDto { Valor = val };
        }

        throw new JsonException($"Unexpected token {reader.TokenType} for DinheiroDto.");
    }

    public override void Write(Utf8JsonWriter writer, DinheiroDto value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value.Valor);
}

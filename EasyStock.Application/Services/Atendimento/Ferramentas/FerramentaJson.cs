using System.Text.Encodings.Web;
using System.Text.Json;

namespace EasyStock.Application.Services.Atendimento.Ferramentas;

/// <summary>Serialização do <c>tool_result</c>: camelCase, acentos legíveis, sem nulos.</summary>
internal static class FerramentaJson
{
    private static readonly JsonSerializerOptions Opcoes = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serializar(object valor) => JsonSerializer.Serialize(valor, Opcoes);

    public static string? LerTexto(JsonElement entrada, string propriedade) =>
        entrada.ValueKind == JsonValueKind.Object
        && entrada.TryGetProperty(propriedade, out var valor)
        && valor.ValueKind == JsonValueKind.String
            ? valor.GetString()
            : null;

    public static string FormatarReais(decimal valor) => valor.ToString("C", new System.Globalization.CultureInfo("pt-BR"));
}

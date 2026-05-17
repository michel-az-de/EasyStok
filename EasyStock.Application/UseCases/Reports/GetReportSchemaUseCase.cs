using System.Reflection;
using System.Text.Json;
using EasyStock.Application.Reporting;
using EasyStock.Domain.Reporting;

namespace EasyStock.Application.UseCases.Reports;

/// <summary>
/// Retorna o JSON Schema (draft-07 simplificado) dos parâmetros de um relatório.
/// Gerado por reflexão sobre <see cref="IReportDefinition.ParamsType"/>
/// (sem dependência de NJsonSchema — geração inline via mapeamento de tipos CLR).
/// </summary>
public sealed class GetReportSchemaUseCase(ReportRegistry registry)
{
    public Task<ReportSchemaDto?> ExecuteAsync(
        string reportKey, CancellationToken ct = default)
    {
        var definition = registry.Find(reportKey);
        if (definition is null)
            return Task.FromResult<ReportSchemaDto?>(null);

        var paramsSchema = BuildJsonSchema(definition.ParamsType);

        return Task.FromResult<ReportSchemaDto?>(new ReportSchemaDto(
            Key:              definition.Key,
            Label:            definition.Label,
            Descricao:        definition.Descricao,
            ParamsSchema:     paramsSchema,
            SupportedFormats: definition.FormatosSuportados.Select(f => f.ToString()).ToList()));
    }

    // ── JSON Schema builder ───────────────────────────────────────────────────

    private static Dictionary<string, object> BuildJsonSchema(Type type)
    {
        var schema = new Dictionary<string, object>
        {
            ["$schema"]    = "http://json-schema.org/draft-07/schema#",
            ["type"]       = "object",
            ["title"]      = type.Name,
            ["properties"] = BuildProperties(type, out var required),
        };

        if (required.Count > 0)
            schema["required"] = required;

        return schema;
    }

    private static Dictionary<string, object> BuildProperties(Type type, out List<string> required)
    {
        required = new List<string>();

        // Records: use primary ctor to determine which params lack defaults (required)
        var primaryCtor = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .OrderByDescending(c => c.GetParameters().Length)
            .FirstOrDefault();

        var requiredNames = (primaryCtor?.GetParameters() ?? [])
            .Where(p => !p.HasDefaultValue)
            .Select(p => p.Name!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var properties = new Dictionary<string, object>();

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var camel = JsonNamingPolicy.CamelCase.ConvertName(prop.Name);
            properties[camel] = MapType(prop.PropertyType);

            if (requiredNames.Contains(prop.Name))
                required.Add(camel);
        }

        return properties;
    }

    private static Dictionary<string, object> MapType(Type type)
    {
        // Nullable<T>
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null)
        {
            return new Dictionary<string, object>
            {
                ["anyOf"] = new object[]
                {
                    MapType(underlying),
                    new Dictionary<string, object> { ["type"] = "null" }
                }
            };
        }

        // Primitives
        if (type == typeof(bool))           return Scalar("boolean");
        if (type == typeof(int)
            || type == typeof(long)
            || type == typeof(short))       return Scalar("integer");
        if (type == typeof(decimal)
            || type == typeof(float)
            || type == typeof(double))      return Scalar("number");
        if (type == typeof(Guid))           return StringFormat("uuid");
        if (type == typeof(DateOnly))       return StringFormat("date");
        if (type == typeof(DateTime)
            || type == typeof(DateTimeOffset)) return StringFormat("date-time");
        if (type == typeof(string))         return Scalar("string");

        // Enums
        if (type.IsEnum)
        {
            return new Dictionary<string, object>
            {
                ["type"]  = "string",
                ["enum"]  = Enum.GetNames(type),
                ["title"] = type.Name
            };
        }

        // Fallback
        return Scalar("string");
    }

    private static Dictionary<string, object> Scalar(string jsonType) =>
        new() { ["type"] = jsonType };

    private static Dictionary<string, object> StringFormat(string format) =>
        new() { ["type"] = "string", ["format"] = format };
}

/// <summary>DTO de resposta do schema de parâmetros.</summary>
public sealed record ReportSchemaDto(
    string             Key,
    string             Label,
    string             Descricao,
    Dictionary<string, object> ParamsSchema,
    List<string>       SupportedFormats);

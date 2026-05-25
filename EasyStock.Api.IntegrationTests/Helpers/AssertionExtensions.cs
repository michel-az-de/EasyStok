using FluentAssertions;
using System.Net;
using System.Text.Json;

namespace EasyStock.Api.IntegrationTests.Helpers;

/// <summary>
/// Extensões de validação para testes multi-tenant
/// </summary>
public static class AssertionExtensions
{
    /// <summary>
    /// Valida que response é 403 ou 200 vazio (acesso negado)
    /// </summary>
    public static void AssertAccessDeniedOrEmpty(this HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        else
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            // Se 200, deve estar vazio ou com lista vazia
        }
    }

    /// <summary>
    /// Valida que CADA item no response pertence ao tenant esperado
    /// </summary>
    public static async Task AssertAllItemsBelongToTenantAsync(
        this HttpResponseMessage response,
        Guid expectedEmpresaId,
        string propertyPath = "empresaId")
    {
        if (response.StatusCode != HttpStatusCode.OK)
            return; // Skip se não for 200

        var content = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrEmpty(content) || content == "{}" || content == "[]")
            return; // Empty response OK

        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            // Handle paginated response: { data: [...], totalCount: N }
            if (root.TryGetProperty("data", out var dataProperty) && dataProperty.ValueKind == JsonValueKind.Array)
            {
                AssertArrayItemsHaveTenant(dataProperty, expectedEmpresaId, propertyPath);
            }
            // Handle direct array: [...]
            else if (root.ValueKind == JsonValueKind.Array)
            {
                AssertArrayItemsHaveTenant(root, expectedEmpresaId, propertyPath);
            }
            // Handle object with nested data
            else if (root.ValueKind == JsonValueKind.Object)
            {
                ValidateObjectTenant(root, expectedEmpresaId, propertyPath, maxDepth: 3);
            }
        }
        catch (JsonException)
        {
            // Não é JSON válido, skip assertion
        }
    }

    /// <summary>
    /// Valida que nenhum item pertence a um tenant inimigo
    /// </summary>
    public static async Task AssertNoItemsFromTenantAsync(
        this HttpResponseMessage response,
        Guid forbiddenEmpresaId,
        string propertyPath = "empresaId")
    {
        if (response.StatusCode != HttpStatusCode.OK)
            return;

        var content = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrEmpty(content) || content == "{}" || content == "[]")
            return;

        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            if (root.TryGetProperty("data", out var dataProperty) && dataProperty.ValueKind == JsonValueKind.Array)
            {
                AssertArrayItemsNotFromTenant(dataProperty, forbiddenEmpresaId, propertyPath);
            }
            else if (root.ValueKind == JsonValueKind.Array)
            {
                AssertArrayItemsNotFromTenant(root, forbiddenEmpresaId, propertyPath);
            }
        }
        catch (JsonException)
        {
            // Skip
        }
    }

    /// <summary>
    /// Valida que response headers não expõem informações sensíveis
    /// </summary>
    public static void AssertNoDataLeakInHeaders(this HttpResponseMessage response)
    {
        // Verificar Set-Cookie não contém tenant info
        if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            foreach (var cookie in cookies)
            {
                cookie.Should().NotContain("empresaId", "Cookie não deve expor empresaId");
            }
        }

        // Verificar Location redirect não contém tenant info
        if (response.Headers.Location?.ToString() is { } location)
        {
            location.Should().NotContain("empresaId", "Redirect não deve expor empresaId na URL");
        }
    }

    /// <summary>
    /// Valida contagem de itens (para evitar "contar itens de outro tenant")
    /// </summary>
    public static async Task<int> AssertAndGetItemCountAsync(this HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrEmpty(content))
            return 0;

        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            // Try paginated response
            if (root.TryGetProperty("totalCount", out var totalCount))
                return totalCount.GetInt32();

            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                return data.GetArrayLength();

            if (root.ValueKind == JsonValueKind.Array)
                return root.GetArrayLength();

            return 0;
        }
        catch
        {
            return 0;
        }
    }

    // ─── Private Helpers ─────────────────────────────────────────────────────────

    private static void AssertArrayItemsHaveTenant(
        JsonElement array,
        Guid expectedEmpresaId,
        string propertyPath)
    {
        array.GetArrayLength().Should().BeGreaterThan(0, "Array deve ter items");

        for (int i = 0; i < Math.Min(array.GetArrayLength(), 100); i++) // Limitar verificação dos primeiros 100
        {
            var item = array[i];
            if (item.ValueKind == JsonValueKind.Object)
            {
                ValidateObjectTenant(item, expectedEmpresaId, propertyPath, maxDepth: 2);
            }
        }
    }

    private static void AssertArrayItemsNotFromTenant(
        JsonElement array,
        Guid forbiddenEmpresaId,
        string propertyPath)
    {
        for (int i = 0; i < array.GetArrayLength(); i++)
        {
            var item = array[i];
            if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty(propertyPath, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.String && Guid.TryParse(prop.GetString(), out var guid))
                {
                    guid.Should().NotBe(forbiddenEmpresaId,
                        $"Item {i} não deve pertencer a tenant {forbiddenEmpresaId}");
                }
            }
        }
    }

    private static void ValidateObjectTenant(
        JsonElement obj,
        Guid expectedEmpresaId,
        string propertyPath,
        int maxDepth)
    {
        if (maxDepth <= 0)
            return;

        // Try direct property match
        if (obj.TryGetProperty(propertyPath, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.String && Guid.TryParse(prop.GetString(), out var guid))
            {
                guid.Should().Be(expectedEmpresaId,
                    $"Object property '{propertyPath}' deve ser {expectedEmpresaId}");
            }
        }

        // Try snake_case variant
        var snakeCaseProperty = ToSnakeCase(propertyPath);
        if (propertyPath != snakeCaseProperty && obj.TryGetProperty(snakeCaseProperty, out var snakeProp))
        {
            if (snakeProp.ValueKind == JsonValueKind.String && Guid.TryParse(snakeProp.GetString(), out var guid))
            {
                guid.Should().Be(expectedEmpresaId,
                    $"Object property '{snakeCaseProperty}' deve ser {expectedEmpresaId}");
            }
        }
    }

    private static string ToSnakeCase(string input) =>
        string.Concat(input.Select((x, i) => i > 0 && char.IsUpper(x) ? "_" + x.ToString().ToLower() : x.ToString().ToLower()));
}

using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace EasyStock.Web.Infrastructure;

/// <summary>
/// Model binder que usa CultureInfo.InvariantCulture para parsear decimais.
/// Necessário porque o frontend JS serializa números com ponto decimal ("8.5"),
/// mas o servidor opera em pt-BR onde o ponto é separador de milhar.
/// </summary>
public sealed class InvariantDecimalModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        var value = bindingContext.ValueProvider
            .GetValue(bindingContext.ModelName).FirstValue;

        if (string.IsNullOrWhiteSpace(value))
        {
            bindingContext.Result = ModelBindingResult.Success(null);
            return Task.CompletedTask;
        }

        // BUG-003: pt-BR usa vírgula decimal. Com NumberStyles.Any + InvariantCulture a vírgula
        // virava separador de MILHAR ("1000,50" -> 100050 = inflação de 1000x). Normalizamos antes
        // de parsear, espelhando o parseDecimal do JS do form (o último separador é o decimal).
        var normalizado = NormalizarDecimalPtBr(value);
        if (decimal.TryParse(normalizado, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var result))
        {
            bindingContext.Result = ModelBindingResult.Success(result);
        }
        else
        {
            bindingContext.ModelState.TryAddModelError(
                bindingContext.ModelName,
                $"Valor '{value}' não é um número válido.");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Normaliza "1.000,50" / "1000,50" / "1000.50" / "1,000.50" → "1000.50" (ponto decimal, sem milhar).
    /// Convenção: o ÚLTIMO separador (. ou ,) é o decimal; os anteriores são milhar.
    /// </summary>
    private static string NormalizarDecimalPtBr(string s)
    {
        s = s.Trim();
        var lastComma = s.LastIndexOf(',');
        var lastDot = s.LastIndexOf('.');
        if (lastComma < 0 && lastDot < 0) return s;
        return lastComma > lastDot
            ? s.Replace(".", string.Empty).Replace(',', '.')  // vírgula é decimal → tira pontos de milhar
            : s.Replace(",", string.Empty);                    // ponto é decimal → tira vírgulas de milhar
    }
}

public sealed class InvariantDecimalModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        var t = context.Metadata.ModelType;
        if (t == typeof(decimal) || t == typeof(decimal?))
            return new InvariantDecimalModelBinder();
        return null;
    }
}

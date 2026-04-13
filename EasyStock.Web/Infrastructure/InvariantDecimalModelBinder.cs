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

        if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result))
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

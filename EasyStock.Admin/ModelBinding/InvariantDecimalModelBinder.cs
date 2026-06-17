using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace EasyStock.Admin.ModelBinding;

/// <summary>
/// Liga decimal/double/float parseando com <see cref="CultureInfo.InvariantCulture"/>.
///
/// Por quê: o Admin roda em pt-BR (Program.cs define DefaultThreadCurrentCulture), cujo
/// separador decimal é a vírgula. Mas <c>&lt;input type="number"&gt;</c> SEMPRE submete o
/// valor com PONTO (formato HTML5), e o model binder padrão usa a cultura corrente — então
/// "19.90"/"20.00" lançam no bind, o parâmetro decimal cai em 0 e o save corrompe (plano
/// salvo R$ 0,00) ou é bloqueado (cupom/fatura). Parsear invariante resolve criação e edição
/// de todos os campos decimais do Admin de uma vez. As query strings já eram ligadas
/// invariante (QueryStringValueProvider), então o comportamento delas não muda. Issue #634.
/// </summary>
public sealed class InvariantDecimalModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        ArgumentNullException.ThrowIfNull(bindingContext);

        var modelName = bindingContext.ModelName;
        var valueResult = bindingContext.ValueProvider.GetValue(modelName);
        if (valueResult == ValueProviderResult.None)
            return Task.CompletedTask; // sem valor submetido: deixa o binding padrão agir

        bindingContext.ModelState.SetModelValue(modelName, valueResult);

        var raw = valueResult.FirstValue;
        var modelType = bindingContext.ModelType;
        var isNullable = Nullable.GetUnderlyingType(modelType) is not null;
        var underlying = Nullable.GetUnderlyingType(modelType) ?? modelType;

        if (string.IsNullOrWhiteSpace(raw))
        {
            // vazio: nullable -> null (válido); non-nullable -> deixa o default agir.
            if (isNullable)
                bindingContext.Result = ModelBindingResult.Success(null);
            return Task.CompletedTask;
        }

        // type=number sempre envia ponto; NumberStyles.Number + InvariantCulture cobre "19.90",
        // "20.00", "20" e o caso en-US com separador de milhar. Não usa pt-BR como fallback de
        // propósito: "20,00" parsearia como 2000 (vírgula vira separador de milhar no invariante).
        const NumberStyles styles = NumberStyles.Number;
        var ci = CultureInfo.InvariantCulture;
        object? value = null;
        var parsed = false;

        if (underlying == typeof(decimal) && decimal.TryParse(raw, styles, ci, out var dec)) { value = dec; parsed = true; }
        else if (underlying == typeof(double) && double.TryParse(raw, styles, ci, out var dbl)) { value = dbl; parsed = true; }
        else if (underlying == typeof(float) && float.TryParse(raw, styles, ci, out var flt)) { value = flt; parsed = true; }

        if (!parsed)
        {
            bindingContext.ModelState.TryAddModelError(modelName, $"O valor '{raw}' não é um número válido.");
            return Task.CompletedTask;
        }

        bindingContext.Result = ModelBindingResult.Success(value);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Aplica o <see cref="InvariantDecimalModelBinder"/> a decimal/double/float (e seus
/// nullable). Inserido no início de <c>MvcOptions.ModelBinderProviders</c> do Admin para
/// ter prioridade sobre o SimpleTypeModelBinder padrão. Issue #634.
/// </summary>
public sealed class InvariantDecimalModelBinderProvider : IModelBinderProvider
{
    private static readonly InvariantDecimalModelBinder Binder = new();

    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var t = Nullable.GetUnderlyingType(context.Metadata.ModelType) ?? context.Metadata.ModelType;
        return t == typeof(decimal) || t == typeof(double) || t == typeof(float) ? Binder : null;
    }
}

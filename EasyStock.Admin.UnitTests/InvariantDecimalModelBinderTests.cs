using EasyStock.Admin.ModelBinding;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Primitives;

namespace EasyStock.Admin.UnitTests;

/// <summary>
/// BUG-02 (#634): &lt;input type=number&gt; submete decimal com PONTO, mas o Admin roda em
/// pt-BR e o binder padrao LANCA em "19.90" (decimal cai em 0 -> plano salvo R$ 0,00 / cupom
/// bloqueado). O <see cref="InvariantDecimalModelBinder"/> liga invariante. Trava o contrato.
/// </summary>
public class InvariantDecimalModelBinderTests
{
    private sealed class StubValueProvider(string key, string? value) : IValueProvider
    {
        public bool ContainsPrefix(string prefix) =>
            value is not null && string.Equals(prefix, key, StringComparison.OrdinalIgnoreCase);

        public ValueProviderResult GetValue(string requested) =>
            value is not null && string.Equals(requested, key, StringComparison.OrdinalIgnoreCase)
                ? new ValueProviderResult(new StringValues(value))
                : ValueProviderResult.None;
    }

    private static async Task<(ModelBindingResult Result, ModelStateDictionary State)> Bind(Type modelType, string? value)
    {
        var ctx = new DefaultModelBindingContext
        {
            ModelMetadata = new EmptyModelMetadataProvider().GetMetadataForType(modelType),
            ModelName = "valor",
            ModelState = new ModelStateDictionary(),
            ValueProvider = new StubValueProvider("valor", value),
        };
        await new InvariantDecimalModelBinder().BindModelAsync(ctx);
        return (ctx.Result, ctx.ModelState);
    }

    [Theory]
    [InlineData("19.90", "19.90")]   // valor fracionario tipico de type=number
    [InlineData("20.00", "20.00")]   // percentual/preco com .00
    [InlineData("20", "20")]         // inteiro
    [InlineData("1234.56", "1234.56")]
    [InlineData("0.01", "0.01")]
    public async Task Liga_decimal_com_ponto_via_invariante(string entrada, string esperado)
    {
        var (result, state) = await Bind(typeof(decimal), entrada);
        result.IsModelSet.Should().BeTrue();
        result.Model.Should().Be(decimal.Parse(esperado, System.Globalization.CultureInfo.InvariantCulture));
        // IsValid fica Unvalidated pos-bind (validacao roda depois); o sinal de bind OK e ErrorCount.
        state.ErrorCount.Should().Be(0);
    }

    [Fact]
    public async Task Nullable_vazio_liga_como_null_sem_erro()
    {
        var (result, state) = await Bind(typeof(decimal?), "");
        result.IsModelSet.Should().BeTrue();
        result.Model.Should().BeNull();
        state.ErrorCount.Should().Be(0);
    }

    [Fact]
    public async Task Valor_nao_numerico_falha_o_bind_e_registra_erro()
    {
        var (result, state) = await Bind(typeof(decimal), "abc");
        result.IsModelSet.Should().BeFalse();
        state.IsValid.Should().BeFalse();
        state.ErrorCount.Should().Be(1);
    }

    [Fact]
    public async Task Double_com_ponto_tambem_liga_invariante()
    {
        var (result, _) = await Bind(typeof(double), "1.5");
        result.IsModelSet.Should().BeTrue();
        result.Model.Should().Be(1.5d);
    }
}

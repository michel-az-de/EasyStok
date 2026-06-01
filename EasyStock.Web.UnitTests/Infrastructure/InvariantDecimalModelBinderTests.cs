using System.Globalization;
using EasyStock.Web.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace EasyStock.Web.UnitTests.Infrastructure;

/// <summary>
/// Trava o parsing invariante de decimais no model binding (defende contra o bug ~100×):
/// sob cultura pt-BR do servidor, um decimal postado tem que ser lido com PONTO decimal
/// ("1234.56" = 1234,56), nunca interpretado como separador de milhar ("1234.56" → 123456).
/// O input HTML <c>type=number</c> sempre posta no formato invariante; este binder garante a
/// leitura correta independente da cultura, e o formato pt-BR cru ("1.234,56") é REJEITADO em
/// vez de silenciosamente virar 123456.
/// </summary>
public class InvariantDecimalModelBinderTests
{
    [Theory]
    [InlineData("1234.56", "1234.56")]
    [InlineData("50.00", "50.00")]
    [InlineData("0.01", "0.01")]
    public async Task Parseia_ponto_decimal_como_invariante(string raw, string esperado)
    {
        var ctx = await BindAsync(raw);
        ctx.Result.IsModelSet.Should().BeTrue();
        ctx.Result.Model.Should().Be(decimal.Parse(esperado, CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Valor_vazio_vira_null_sem_erro()
    {
        var ctx = await BindAsync("");
        ctx.Result.IsModelSet.Should().BeTrue();
        ctx.Result.Model.Should().BeNull();
        ctx.ModelState.ErrorCount.Should().Be(0);
    }

    [Theory]
    [InlineData("1.234,56")]   // formato pt-BR: deve REJEITAR, não virar 123456
    [InlineData("abc")]
    public async Task Rejeita_formato_ptBR_ou_invalido(string raw)
    {
        var ctx = await BindAsync(raw);
        ctx.Result.IsModelSet.Should().BeFalse();
        ctx.ModelState.ErrorCount.Should().BeGreaterThan(0);
    }

    private static async Task<DefaultModelBindingContext> BindAsync(string? raw)
    {
        var binder = new InvariantDecimalModelBinder();
        var ctx = new DefaultModelBindingContext
        {
            ModelName = "valor",
            ModelState = new ModelStateDictionary(),
            ValueProvider = new StubValueProvider("valor", raw),
        };
        await binder.BindModelAsync(ctx);
        return ctx;
    }

    private sealed class StubValueProvider(string key, string? value) : IValueProvider
    {
        public bool ContainsPrefix(string prefix) => true;

        public ValueProviderResult GetValue(string k)
        {
            if (k == key && value is not null)
                return new ValueProviderResult(value);
            return ValueProviderResult.None;
        }
    }
}

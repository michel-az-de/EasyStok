using System.Globalization;
using EasyStock.Web.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace EasyStock.Web.UnitTests.Infrastructure;

/// <summary>
/// Trava o parsing de decimais no model binding (defende contra o bug ~100×): sob cultura
/// pt-BR do servidor, NumberStyles.Any + InvariantCulture leria a vírgula que o form realmente
/// posta ("49,90") como separador de milhar → 4990. O binder normaliza antes (convenção: o
/// ÚLTIMO separador "." ou "," é o decimal; os anteriores são milhar), aceitando tanto o formato
/// invariante do JS ("1234.56") quanto o pt-BR cru ("49,90", "1.234,56" → 1234.56). Só entradas
/// sem número ("abc") são rejeitadas. Ver BUG-003 (stale-test pego na revisão adversarial 2026-06-18).
/// </summary>
public class InvariantDecimalModelBinderTests
{
    [Theory]
    [InlineData("1234.56", "1234.56")]   // invariante (ex: JS / type=number)
    [InlineData("50.00", "50.00")]
    [InlineData("0.01", "0.01")]
    public async Task Parseia_ponto_decimal_como_invariante(string raw, string esperado)
    {
        var ctx = await BindAsync(raw);
        ctx.Result.IsModelSet.Should().BeTrue();
        ctx.Result.Model.Should().Be(decimal.Parse(esperado, CultureInfo.InvariantCulture));
    }

    // BUG-003: o form pt-BR posta vírgula decimal ("49,90"); o binder normaliza
    // (último separador = decimal) em vez de tratar a vírgula como milhar (→ 4990).
    [Theory]
    [InlineData("49,90", "49.90")]       // vírgula decimal crua do form
    [InlineData("1000,50", "1000.50")]   // vírgula NÃO é milhar
    [InlineData("10,5", "10.5")]
    [InlineData("1.234,56", "1234.56")]  // ponto de milhar + vírgula decimal
    [InlineData("1,234.56", "1234.56")]  // formato US (vírgula de milhar + ponto decimal)
    public async Task Parseia_virgula_decimal_ptBR(string raw, string esperado)
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
    [InlineData("abc")]
    [InlineData("R$ 10")]
    public async Task Rejeita_invalido(string raw)
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

using System.Text.Json;
using EasyStock.Api.Mobile.DTOs;
using FluentAssertions;

namespace EasyStock.Api.UnitTests.Mobile;

/// <summary>
/// #2718: contrato do saldo no JSON do sync. O Postgres devolve numeric(18,3) com escala 3 (7.000);
/// o aparelho sempre recebeu inteiro e continua recebendo "7", não "7.000". Fracionado sai "1.5".
/// </summary>
public sealed class SaldoSemZerosFinaisJsonTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static ProductDto Produto(decimal stock) =>
        new("p1", "Lasanha", null, "massa", "un", 30m, stock, null);

    [Theory]
    [InlineData("7.000", "7")]
    [InlineData("1", "1")]
    [InlineData("0.000", "0")]
    [InlineData("1.500", "1.5")]
    [InlineData("0.250", "0.25")]
    [InlineData("-2.000", "-2")]
    [InlineData("2147483647.000", "2147483647")]
    public void Saldo_sai_sem_zeros_finais(string doBanco, string esperado)
    {
        var stock = decimal.Parse(doBanco, System.Globalization.CultureInfo.InvariantCulture);

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(Produto(stock), Json));

        doc.RootElement.GetProperty("stock").GetRawText().Should().Be(esperado);
    }

    [Theory]
    [InlineData("3", 3)]
    [InlineData("1.5", 1.5)]
    public void Aparelho_que_manda_inteiro_ou_fracionado_e_aceito(string noJson, double esperado)
    {
        var dto = JsonSerializer.Deserialize<ProductDto>(
            $$"""{"id":"p1","name":"Lasanha","category":"massa","stock":{{noJson}}}""", Json)!;

        dto.Stock.Should().Be((decimal)esperado);
    }
}

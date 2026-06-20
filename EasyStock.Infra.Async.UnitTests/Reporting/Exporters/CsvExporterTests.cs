using System.Text;
using EasyStock.Application.Reporting;
using EasyStock.Domain.Reporting;
using EasyStock.Infra.Async.Reporting.Exporters;
using FluentAssertions;

namespace EasyStock.Infra.Async.UnitTests.Reporting.Exporters;

// PROD-01: CsvExporter precisa neutralizar CSV/formula injection (= @ + - TAB CR), igual ao
// helper canonico Csv.Field. Sem isso, "=CMD()" num campo de tenant vira formula no Excel.
public class CsvExporterTests
{
    private sealed class LinhaFake
    {
        public string? Nome { get; init; }
        public decimal Total { get; init; }
    }

    private static ReportSchema Schema() => new(
        title: "Teste",
        fileNameBase: "teste",
        columns:
        [
            new("Nome",  "Nome",  0),
            new("Total", "Total", 1, "0.00"),
        ]);

    private static async IAsyncEnumerable<LinhaFake> Rows(IEnumerable<LinhaFake> items)
    {
        foreach (var it in items) { await Task.Yield(); yield return it; }
    }

    private static async Task<string> Exportar(IEnumerable<LinhaFake> linhas)
    {
        using var output = new MemoryStream();
        await new CsvExporter().WriteAsync(
            Rows(linhas), Schema(), output, new ReportExportOptions(), CancellationToken.None);

        var bytes = output.ToArray();
        var bom = Encoding.UTF8.GetPreamble();
        var start = bytes.Length >= bom.Length && bytes.Take(bom.Length).SequenceEqual(bom) ? bom.Length : 0;
        return Encoding.UTF8.GetString(bytes, start, bytes.Length - start);
    }

    [Theory]
    [InlineData("=SUM(A1:A2)")]
    [InlineData("+1+1")]
    [InlineData("-2+3")]
    [InlineData("@cmd")]
    public async Task WriteAsync_NeutralizaFormulaInjection(string perigoso)
    {
        var csv = await Exportar(new[] { new LinhaFake { Nome = perigoso, Total = 10m } });

        // CsvHelper InjectionOptions.Escape prefixa o campo perigoso com ' (apostrofo).
        csv.Should().Contain("'" + perigoso);
    }

    [Fact]
    public async Task WriteAsync_NaoMexeEmCampoBenigno()
    {
        var csv = await Exportar(new[] { new LinhaFake { Nome = "Lasanha", Total = 10m } });

        csv.Should().Contain("Lasanha");
        csv.Should().NotContain("'Lasanha");
    }
}

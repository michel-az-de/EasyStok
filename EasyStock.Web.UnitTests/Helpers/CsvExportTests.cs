using System.Text;
using EasyStock.Web.Helpers;
using FluentAssertions;

namespace EasyStock.Web.UnitTests.Helpers;

// #612: CsvExport e o espelho Web do helper canonico EasyStock.Application.Common.Csv.
// Estes testes travam o anti-injecao de formula + quoting RFC-4180 (mesma cobertura do
// CsvTests do backend), evitando drift entre os dois.
public class CsvExportTests
{
    private static string Decode(byte[] bytes)
    {
        var bom = Encoding.UTF8.GetPreamble().Length;
        return Encoding.UTF8.GetString(bytes, bom, bytes.Length - bom);
    }

    [Theory]
    [InlineData("=SUM(A1:A2)")]
    [InlineData("+1")]
    [InlineData("-1+2")]
    [InlineData("@cmd")]
    public void Field_neutraliza_injecao_de_formula(string perigoso)
    {
        CsvExport.Field(perigoso).Should().StartWith("'");
    }

    [Fact]
    public void Field_faz_quoting_rfc4180()
    {
        CsvExport.Field("a;b").Should().Be("\"a;b\"");
        CsvExport.Field("diz \"oi\"").Should().Be("\"diz \"\"oi\"\"\"");
        CsvExport.Field("linha1\nlinha2").Should().Be("\"linha1\nlinha2\"");
    }

    [Fact]
    public void Field_texto_simples_inalterado()
    {
        CsvExport.Field("Bolo de cenoura").Should().Be("Bolo de cenoura");
        CsvExport.Field(null).Should().Be("");
        CsvExport.Field("").Should().Be("");
    }

    [Fact]
    public void Build_usa_bom_separador_ponto_e_virgula_e_terminador_crlf()
    {
        var bytes = CsvExport.Build(
            new[] { "A", "B" },
            new[] { new[] { "1", "2" }, new[] { "x;y", "=z" } });

        bytes.Take(3).Should().Equal(Encoding.UTF8.GetPreamble());
        Decode(bytes).Should().Be("A;B\r\n1;2\r\n\"x;y\";'=z\r\n");
    }
}

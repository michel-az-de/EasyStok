using System.Text;
using EasyStock.Application.Common;

namespace EasyStock.Application.Tests.Common;

public class CsvTests
{
    private static string SemBom(byte[] bytes) => Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

    [Fact]
    public void Build_prefixa_BOM_utf8()
    {
        var bytes = Csv.Build(new[] { "A" }, Array.Empty<IReadOnlyList<string>>());
        bytes.Take(3).Should().Equal((byte)0xEF, (byte)0xBB, (byte)0xBF);
    }

    [Fact]
    public void Build_header_sem_linhas_gera_so_header_com_CRLF()
    {
        var bytes = Csv.Build(new[] { "A", "B" }, Array.Empty<IReadOnlyList<string>>());
        SemBom(bytes).Should().Be("A;B\r\n");
    }

    [Fact]
    public void Build_termina_cada_linha_inclusive_a_ultima_com_CRLF()
    {
        var rows = new[] { (IReadOnlyList<string>)new[] { "1", "2" } };
        var bytes = Csv.Build(new[] { "A", "B" }, rows);
        SemBom(bytes).Should().Be("A;B\r\n1;2\r\n");
    }

    [Theory]
    [InlineData("a;b", "\"a;b\"")]
    [InlineData("a\"b", "\"a\"\"b\"")]
    [InlineData("a\nb", "\"a\nb\"")]
    [InlineData("normal", "normal")]
    [InlineData("", "")]
    public void Field_aplica_quoting_RFC4180(string raw, string esperado)
    {
        Csv.Field(raw).Should().Be(esperado);
    }

    [Fact]
    public void Field_null_vira_vazio()
    {
        Csv.Field(null).Should().BeEmpty();
    }

    [Theory]
    [InlineData("=SUM(A1)", "'=SUM(A1)")]
    [InlineData("+1+1", "'+1+1")]
    [InlineData("-2+3", "'-2+3")]
    [InlineData("@cmd", "'@cmd")]
    [InlineData("\tx", "'\tx")]
    [InlineData("\rx", "\"'\rx\"")] // CR também dispara quoting RFC-4180
    public void Field_neutraliza_injecao_de_formula(string perigoso, string esperado)
    {
        // Prefixo de aspa simples — Excel/Sheets tratam como texto literal, não executam.
        Csv.Field(perigoso).Should().Be(esperado);
    }

    // Paridade de quoting com o Csv() privado de ExportarFaturasCsvUseCase: para
    // valores sem prefixo perigoso, o escaping precisa ser byte-idêntico.
    [Theory]
    [InlineData("Casa da Baba")]
    [InlineData("Bar; Grill")]
    [InlineData("aspas \"internas\"")]
    [InlineData("quebra\nlinha")]
    [InlineData("")]
    public void Field_tem_paridade_de_quoting_com_Faturas(string valor)
    {
        Csv.Field(valor).Should().Be(FaturasCsv(valor));
    }

    /// <summary>Réplica fiel do Csv() privado de ExportarFaturasCsvUseCase.</summary>
    private static string FaturasCsv(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        if (raw.Contains(';') || raw.Contains('"') || raw.Contains('\n') || raw.Contains('\r'))
            return "\"" + raw.Replace("\"", "\"\"") + "\"";
        return raw;
    }
}

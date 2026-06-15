using System.Text;

namespace EasyStock.Web.Helpers;

/// <summary>
/// Espelho de <c>EasyStock.Application.Common.Csv</c> para o Web. O Web e BFF HTTP puro
/// (sem ProjectReference a Application/Domain), entao nao da pra reusar o helper canonico
/// do backend — esta copia replica o MESMO comportamento e deve ser mantida em sincronia
/// (#612). Saida compativel com Excel pt-BR: BOM UTF-8, separador <c>;</c>, terminador
/// <c>\r\n</c> (deterministico). Cada campo passa por <see cref="Field"/>: endurecimento
/// contra injecao de formula (prefixo <c>'</c> quando inicia com <c>= + - @ TAB CR</c>)
/// seguido de quoting RFC-4180.
/// </summary>
public static class CsvExport
{
    private const char Separator = ';';
    private const string LineTerminator = "\r\n";

    /// <summary>Monta o CSV completo (com BOM) a partir do cabecalho e das linhas.</summary>
    public static byte[] Build(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
    {
        var sb = new StringBuilder();
        AppendLine(sb, headers);
        foreach (var row in rows)
            AppendLine(sb, row);

        var bom = Encoding.UTF8.GetPreamble();
        var content = Encoding.UTF8.GetBytes(sb.ToString());
        var result = new byte[bom.Length + content.Length];
        Buffer.BlockCopy(bom, 0, result, 0, bom.Length);
        Buffer.BlockCopy(content, 0, result, bom.Length, content.Length);
        return result;
    }

    private static void AppendLine(StringBuilder sb, IReadOnlyList<string> fields)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            if (i > 0) sb.Append(Separator);
            sb.Append(Field(fields[i]));
        }
        sb.Append(LineTerminator);
    }

    /// <summary>
    /// Escapa e endurece um campo. Anti-formula primeiro (vale mesmo dentro de aspas, pois
    /// o app avalia o valor ja "desquotado"), depois quoting RFC-4180.
    /// </summary>
    public static string Field(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;

        var value = raw;

        var first = value[0];
        if (first is '=' or '+' or '-' or '@' or '\t' or '\r')
            value = "'" + value;

        if (value.IndexOf(Separator) >= 0 || value.IndexOf('"') >= 0
            || value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0)
        {
            value = "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }
}

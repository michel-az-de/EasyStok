using System.Text;

namespace EasyStock.Application.Common;

/// <summary>
/// Construtor de CSV compartilhado pelas exportações do Admin. Saída compatível com
/// Excel pt-BR: BOM UTF-8, separador <c>;</c> e terminador <c>\r\n</c> (RFC-4180,
/// determinístico — <b>não</b> usa <see cref="Environment.NewLine"/>, que varia por SO).
///
/// <para>
/// Cada campo passa por <see cref="Field"/>: endurecimento contra injeção de fórmula
/// (prefixo <c>'</c> quando inicia com <c>= + - @ TAB CR</c>) seguido de quoting
/// RFC-4180 (aspas quando contém <c>; " \n \r</c>, aspas internas duplicadas).
/// </para>
///
/// <para>
/// Equivalente ao <c>Csv()</c> privado de <c>ExportarFaturasCsvUseCase</c> no quoting;
/// difere de propósito em dois pontos: terminador <c>\r\n</c> explícito (Faturas usa
/// <see cref="Environment.NewLine"/>) e o passo anti-fórmula (Faturas não tem).
/// </para>
/// </summary>
public static class Csv
{
    private const char Separator = ';';
    private const string LineTerminator = "\r\n";

    /// <summary>
    /// Monta o conteúdo CSV completo (com BOM) a partir do cabeçalho e das linhas.
    /// Cada linha termina com <c>\r\n</c>, inclusive a última.
    /// </summary>
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
    /// Escapa e endurece um único campo. Anti-fórmula primeiro (vale mesmo dentro de
    /// aspas, pois o app avalia o valor já "desquotado"), depois quoting RFC-4180.
    /// </summary>
    public static string Field(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;

        var value = raw;

        // Anti-injeção de fórmula (Excel/Sheets avaliam células iniciadas por estes
        // caracteres). Prefixa com aspa simples — tratada como texto literal.
        var first = value[0];
        if (first is '=' or '+' or '-' or '@' or '\t' or '\r')
            value = "'" + value;

        // Quoting RFC-4180.
        if (value.IndexOf(Separator) >= 0 || value.IndexOf('"') >= 0
            || value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0)
        {
            value = "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }
}

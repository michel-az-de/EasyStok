using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace EasyStock.Api.UnitTests.Services.Impressao;

/// <summary>Apoio dos testes de impressos: recorte do corpo, cores usadas nele e snapshot em <c>Snapshots/</c>.</summary>
internal static class SnapshotImpresso
{
    public static string Corpo(string html) =>
        html[(html.IndexOf("<body>", StringComparison.Ordinal) + 6)..html.IndexOf("</body>", StringComparison.Ordinal)];

    /// <summary>Cores em atributo ou estilo do corpo (fill="#000", color:#...); "#A7F3C21B" no texto é o pedido.</summary>
    public static IEnumerable<string> CoresDoCorpo(string html) =>
        Regex.Matches(Corpo(html), "[:=]\\s*\"?(#[0-9A-Fa-f]{3,6})\\b").Select(m => m.Groups[1].Value.ToLowerInvariant());

    public static void Conferir(string nome, string atual, [CallerFilePath] string arquivoTeste = "")
    {
        var caminho = Path.Combine(Path.GetDirectoryName(arquivoTeste)!, "Snapshots", nome);
        if (Environment.GetEnvironmentVariable("IMPRESSAO_ATUALIZAR_SNAPSHOT") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
            File.WriteAllText(caminho, atual);
            return;
        }
        File.Exists(caminho).Should().BeTrue($"snapshot {nome} existe (gere com IMPRESSAO_ATUALIZAR_SNAPSHOT=1)");
        atual.ReplaceLineEndings().Should().Be(File.ReadAllText(caminho).ReplaceLineEndings());
    }
}

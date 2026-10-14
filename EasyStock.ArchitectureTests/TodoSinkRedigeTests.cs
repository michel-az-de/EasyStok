using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace EasyStock.ArchitectureTests;

/// <summary>
/// Guard (I4.5, #1698; invariante 16): nenhum sink do Serilog fica sem redação, na API ou no Worker. Antes só o
/// arquivo da API redigia; o console é o log do contêiner em produção e o OTLP sai da máquina. Um sink novo
/// colado direto em <c>WriteTo</c> voltaria a vazar sem nenhum teste de unidade perceber.
///
/// Source-text based (idioma de <see cref="ChaveMestraNosDoisHostsTests"/>).
/// </summary>
[Trait("Category", "Architecture")]
public class TodoSinkRedigeTests
{
    private static readonly string[] SinksQueRedigem = ["Redigido", "RedactedFile"];
    private static readonly Regex ComentarioDeLinha = new(@"//.*$", RegexOptions.Multiline | RegexOptions.Compiled);

    [Theory]
    [InlineData("EasyStock.Api/appsettings.json")]
    [InlineData("EasyStock.Api/appsettings.Production.json")]
    [InlineData("EasyStock.Api/appsettings.Development.json")]
    [InlineData("EasyStock.Worker/appsettings.json")]
    public void Sink_do_appsettings_redige(string arquivo)
    {
        using var json = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(RepoPaths.FindRepoRoot(), arquivo)),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

        if (!json.RootElement.TryGetProperty("Serilog", out var serilog) || !serilog.TryGetProperty("WriteTo", out var sinks))
            return;

        var nomes = sinks.EnumerateArray().Select(s => s.GetProperty("Name").GetString()).ToList();
        nomes.Should().OnlyContain(n => SinksQueRedigem.Contains(n),
            "sink fora do Redigido escreve segredo em claro; envolva-o: {{ \"Name\": \"Redigido\", \"Args\": {{ \"configure\": [ ... ] }} }}");
    }

    [Theory]
    [InlineData("EasyStock.Api/Program.cs")]
    [InlineData("EasyStock.Worker/Program.cs")]
    public void Sink_ligado_no_codigo_redige(string program)
    {
        var codigo = ComentarioDeLinha.Replace(File.ReadAllText(Path.Combine(RepoPaths.FindRepoRoot(), program)), string.Empty);

        var diretos = Regex.Matches(codigo, @"\.WriteTo\s*\.\s*(\w+)\s*\(").Select(m => m.Groups[1].Value).ToList();
        diretos.Should().OnlyContain(n => SinksQueRedigem.Contains(n),
            "use WriteTo.Redigido(w => w.<Sink>(...)) para o sink receber o evento ja redigido");
    }
}
using System.Text.Json;
using FluentAssertions;

namespace EasyStock.Api.UnitTests.Observability.Logging;

/// <summary>
/// Smoke do appsettings.json: garante que o sink File esta usando RedactedFile
/// (B3.0b) e nao o File nativo. Regressao aqui = alguem trocou de volta e
/// o pipeline de redaction write-time silenciosamente parou de proteger logs.
///
/// [Trait Category=SecurityRegression] — falha em CI sem skip silencioso.
/// </summary>
[Trait("Category", "SecurityRegression")]
public class SerilogConfigSmokeTests
{
    [Fact]
    public void AppSettings_DeveUsar_RedactedFile_NoSinkDeArquivo()
    {
        var path = LocateAppSettingsJson();
        using var doc = JsonDocument.Parse(File.ReadAllText(path));

        var serilog = doc.RootElement.GetProperty("Serilog");

        // 1. EasyStock.Api precisa estar no Using array para Serilog encontrar
        //    SerilogRedactionExtensions via reflection.
        var usingArray = serilog.GetProperty("Using").EnumerateArray()
            .Select(e => e.GetString())
            .ToArray();
        usingArray.Should().Contain("EasyStock.Api",
            "sem isso o sink RedactedFile nao e descoberto e Serilog falha no startup");

        // 2. Nenhum sink "File" nativo deve ter sobrado — sink de arquivo precisa
        //    ser exclusivamente RedactedFile (write-time redaction obrigatoria).
        var writeTo = serilog.GetProperty("WriteTo").EnumerateArray().ToArray();
        var fileSinks = writeTo
            .Where(w => w.GetProperty("Name").GetString() == "File")
            .ToArray();
        fileSinks.Should().BeEmpty(
            "sink File nativo escreve sem redaction — usar RedactedFile (B3.0b)");

        // 3. Pelo menos um sink RedactedFile precisa estar configurado para
        //    cobrir o vetor at-rest/shipping.
        var redactedSinks = writeTo
            .Where(w => w.GetProperty("Name").GetString() == "RedactedFile")
            .ToArray();
        redactedSinks.Should().HaveCountGreaterThanOrEqualTo(1,
            "ao menos um sink RedactedFile precisa proteger logs at-rest");
    }

    private static string LocateAppSettingsJson()
    {
        // Test bin -> volta ate achar EasyStock.Api/appsettings.json
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "EasyStock.Api", "appsettings.json");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            "appsettings.json nao encontrado subindo da test bin — estrutura do repo mudou?");
    }
}

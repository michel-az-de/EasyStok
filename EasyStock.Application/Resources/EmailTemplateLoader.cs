using System.Reflection;

namespace EasyStock.Application.Resources;

/// <summary>
/// Carrega templates de email da camada Application via <c>EmbeddedResource</c>.
/// Espelha <c>EasyStock.Api.Data.EmailTemplateLoader</c> — mas pra templates usados
/// em UseCases (que não podem depender de Api).
///
/// Layout no .csproj:
/// <code>
/// &lt;EmbeddedResource Include="Resources\Email\*.html"&gt;
///   &lt;LogicalName&gt;ApplicationEmailTemplates.%(Filename).html&lt;/LogicalName&gt;
/// &lt;/EmbeddedResource&gt;
/// </code>
///
/// Throw específico se o arquivo não foi declarado — pega typos imediatamente.
/// </summary>
public static class EmailTemplateLoader
{
    private const string ResourcePrefix = "ApplicationEmailTemplates.";

    private static readonly Assembly _assembly = typeof(EmailTemplateLoader).Assembly;

    public static string LoadBody(string codigo)
    {
        var resourceName = $"{ResourcePrefix}{codigo}.html";
        using var stream = _assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
            throw new InvalidOperationException(
                $"Application email template embedded resource not found: {resourceName}. " +
                $"Add <EmbeddedResource Include=\"Resources\\Email\\{codigo}.html\"> with " +
                $"<LogicalName>{resourceName}</LogicalName> in EasyStock.Application.csproj.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

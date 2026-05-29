using System.Reflection;

namespace EasyStock.Api.Data;

/// <summary>
/// Carrega o corpo de templates de email a partir de <c>EmbeddedResource</c> declarados
/// no <c>EasyStock.Api.csproj</c>. Cada template HTML vive em
/// <c>EasyStock.Api/Data/Templates/Email/&lt;codigo&gt;.html</c> com
/// <c>&lt;LogicalName&gt;EmailTemplates.&lt;codigo&gt;.html&lt;/LogicalName&gt;</c>.
///
/// Throw específico com o nome do resource ausente quando o arquivo .html não foi
/// declarado no csproj — pega typos de migração imediatamente.
/// </summary>
public static class EmailTemplateLoader
{
    private const string ResourcePrefix = "EmailTemplates.";

    private static readonly Assembly _assembly = typeof(EmailTemplateLoader).Assembly;

    public static string LoadBody(string codigo)
    {
        var resourceName = $"{ResourcePrefix}{codigo}.html";
        using var stream = _assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
            throw new InvalidOperationException(
                $"Email template embedded resource not found: {resourceName}. " +
                $"Add <EmbeddedResource Include=\"Data\\Templates\\Email\\{codigo}.html\"> with " +
                $"<LogicalName>{resourceName}</LogicalName> in EasyStock.Api.csproj.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

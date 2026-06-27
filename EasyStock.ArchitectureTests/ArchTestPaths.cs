namespace EasyStock.ArchitectureTests;

/// <summary>
/// Resolucao de path compartilhada pelos arch-tests de HIGIENE DE DESIGN que leem fonte do repo
/// em runtime (issue #705, Fatia 4). Consolida o walk-up ".sln" antes duplicado em cada teste do
/// cluster (CssHex/Razor/Alpine/TokensDrift, Web e Admin).
///
/// Mecanismo: <see cref="AppContext.BaseDirectory"/> (local do binario de teste). DIFERE do
/// <see cref="RepoPaths"/>, que usa <c>[CallerFilePath]</c> (caminho-fonte em tempo de compilacao)
/// — sao estrategias com trade-offs opostos (BaseDirectory quebra com <c>dotnet test -o tempdir</c>;
/// CallerFilePath quebra com pathmap/SourceLink no CI) e os dois campos coexistem de proposito.
///
/// GOTCHA: por usar BaseDirectory, NAO rode estes testes com <c>dotnet test -o &lt;tempdir&gt;</c>
/// (BaseDirectory fora do repo quebra o walk-up); deixe o Husky/bin real rodar.
/// </summary>
internal static class ArchTestPaths
{
    /// <summary>Raiz da solucao: primeiro ancestral de AppContext.BaseDirectory que contem um *.sln.</summary>
    public static string SolutionRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !current.GetFiles("*.sln").Any())
            current = current.Parent;
        if (current is null)
            throw new InvalidOperationException(
                "Nao foi possivel localizar a raiz da solucao a partir de AppContext.BaseDirectory.");
        return current.FullName;
    }

    /// <summary>Diretorio &lt;raiz&gt;/&lt;project&gt;/&lt;sub...&gt; (ex.: AppDirectory("EasyStock.Admin", "wwwroot", "css")).</summary>
    public static DirectoryInfo AppDirectory(string project, params string[] sub)
    {
        var parts = new List<string> { SolutionRoot(), project };
        parts.AddRange(sub);
        var dir = new DirectoryInfo(Path.Combine(parts.ToArray()));
        if (!dir.Exists)
            throw new InvalidOperationException($"Pasta nao encontrada: {dir.FullName}");
        return dir;
    }

    /// <summary>Le &lt;raiz&gt;/&lt;project&gt;/&lt;sub...&gt; como texto (ex.: ReadAppFile("EasyStock.Web", "wwwroot", "js", "form-modal.js")).</summary>
    public static string ReadAppFile(string project, params string[] sub)
    {
        var parts = new List<string> { SolutionRoot(), project };
        parts.AddRange(sub);
        var path = Path.Combine(parts.ToArray());
        if (!File.Exists(path))
            throw new InvalidOperationException($"Arquivo nao encontrado: {path}");
        return File.ReadAllText(path);
    }

    /// <summary>Caminho relativo (forward slash) de fullPath em relacao a baseDir.</summary>
    public static string ToRelative(DirectoryInfo baseDir, string fullPath) =>
        Path.GetRelativePath(baseDir.FullName, fullPath).Replace(Path.DirectorySeparatorChar, '/');
}

using FluentAssertions;

namespace EasyStock.ArchitectureTests;

public class ProjectFileHygieneTests
{
    [Fact]
    public void ProjectFiles_NaoDevemUsarCompileRemove_ParaEsconderArquivos()
    {
        var solutionRoot = GetSolutionRoot();
        var offenders = Directory
            .GetFiles(solutionRoot.FullName, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}.claude{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => File.ReadAllText(path).Contains("<Compile Remove=", StringComparison.OrdinalIgnoreCase))
            .ToList();

        offenders.Should().BeEmpty("arquivos .cs precisam ser corrigidos ou isolados explicitamente, sem esconder compilacao via csproj");
    }

    private static DirectoryInfo GetSolutionRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null && !current.GetFiles("EasyStok.sln").Any())
            current = current.Parent;

        return current ?? throw new InvalidOperationException("Nao foi possivel localizar a raiz da solucao.");
    }
}

using System.Runtime.CompilerServices;

namespace EasyStock.ArchitectureTests;

/// <summary>
/// Helper para localizar a raiz do repositório partindo do arquivo de teste.
///
/// Usa <see cref="CallerFilePathAttribute"/> e sobe a árvore até encontrar
/// um marker (<c>*.sln</c> ou <c>.git/</c>).
///
/// Atenção: Se o CI passar a usar <c>ContinuousIntegrationBuild=true</c> ou
/// <c>SourceLink</c> com <c>pathmap</c>, o caminho retornado por
/// <c>[CallerFilePath]</c> vira o remapeado e <see cref="FindRepoRoot"/>
/// lança <see cref="InvalidOperationException"/>. Pre-check da F2 confirmou
/// que isso não acontece hoje. Se mudar, fallback é copiar arquivos-alvo
/// pro bin via <c>&lt;None CopyToOutputDirectory&gt;</c> e ler de
/// <c>AppContext.BaseDirectory</c>.
/// </summary>
internal static class RepoPaths
{
    public static string FindRepoRoot([CallerFilePath] string callerPath = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(callerPath)!);
        while (dir != null && !dir.GetFiles("*.sln").Any() && !dir.GetDirectories(".git").Any())
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException(
            $"Repo root not found from caller path '{callerPath}'. " +
            "If CI uses ContinuousIntegrationBuild=true or pathmap, switch this test " +
            "to read from AppContext.BaseDirectory via <None CopyToOutputDirectory>.");
    }
}

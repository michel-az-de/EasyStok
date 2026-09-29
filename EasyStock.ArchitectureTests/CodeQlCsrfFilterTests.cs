using System.Text.RegularExpressions;
using FluentAssertions;

namespace EasyStock.ArchitectureTests;

/// <summary>
/// Guard (ADR-0052, issue #1089): o <c>codeql.yml</c> tira a regra
/// <c>cs/web/missing-token-validation</c> da <c>EasyStock.Api</c> porque a Api autentica por JWT
/// bearer, e CSRF nao se aplica a bearer. A excecao sao os controllers que autenticam por cookie
/// (storefront, ADR-0012): esses precisam continuar analisados, e a lista deles vive no workflow
/// como linhas <c>+arquivo:regra</c> do filter-sarif.
///
/// Sem este teste a lista apodrece em silencio: um controller novo que passa a ler cookie herda a
/// supressao sem ninguem perceber, e o CodeQL, que existe justamente para pegar esse caso, fica
/// cego para ele. Com o teste, usar cookie na Api obriga a editar o workflow, e a edicao aparece no
/// diff da PR.
/// </summary>
[Trait("Category", "Architecture")]
public class CodeQlCsrfFilterTests
{
    private const string Regra = "cs/web/missing-token-validation";
    private const string WorkflowPath = ".github/workflows/codeql.yml";

    private static readonly Regex ExclusaoRegex = new(
        @"^\s*-(?<arquivo>\S+):" + Regex.Escape(Regra) + @"\s*$", RegexOptions.Compiled);

    private static readonly Regex ReinclusaoRegex = new(
        @"^\s*\+(?<arquivo>EasyStock\.Api/\S+\.cs):" + Regex.Escape(Regra) + @"\s*$", RegexOptions.Compiled);

    /// <summary>Leitura ou escrita de cookie, ou opt-in no esquema de sessao por cookie.</summary>
    private static readonly Regex UsoDeCookieRegex = new(
        @"\.Cookies\b|ClienteSessionAuthenticationHandler\.SchemeName|""ClienteSession""", RegexOptions.Compiled);

    /// <summary>
    /// Infra do esquema de sessao por cookie. Nao tem action POST, entao a regra do CodeQL nao
    /// dispara neles; o que importa e quem USA o esquema, e isso o teste pega pelo SchemeName.
    /// </summary>
    private static readonly HashSet<string> InfraDoEsquema = new(StringComparer.OrdinalIgnoreCase)
    {
        "EasyStock.Api/Authentication/ClienteSessionAuthenticationHandler.cs",
        "EasyStock.Api/Configuration/ApiServiceCollectionExtensions.cs",
        "EasyStock.Api/Middleware/ClienteSessionMiddleware.cs",
    };

    [Fact]
    public void Exclusao_Da_Regra_De_Csrf_So_Pode_Valer_Para_A_Api()
    {
        var exclusoes = LinhasDoWorkflow()
            .Select(l => ExclusaoRegex.Match(l))
            .Where(m => m.Success)
            .Select(m => m.Groups["arquivo"].Value)
            .ToList();

        exclusoes.Should().Equal(new[] { "EasyStock.Api/**" },
            "a supressao do CSRF vale so para a Api, que e bearer. EasyStock.Web e EasyStock.Admin " +
            "autenticam por cookie e precisam da regra inteira (ADR-0052).");
    }

    [Fact]
    public void Todo_Arquivo_Da_Api_Que_Usa_Cookie_Deve_Ser_Reincluido_No_Filtro()
    {
        var root = RepoPaths.FindRepoRoot();
        var reincluidos = Reincluidos();
        var faltando = ArquivosDaApiQueUsamCookie(root)
            .Where(rel => !reincluidos.Contains(rel))
            .ToList();

        faltando.Should().BeEmpty(
            "estes arquivos da Api usam cookie e por isso estao sujeitos a CSRF, mas o codeql.yml " +
            "tira a regra deles. Acrescente uma linha '+<arquivo>:" + Regra + "' no filter-sarif " +
            "do " + WorkflowPath + " (ADR-0052, issue #1088).");
    }

    [Fact]
    public void Reinclusao_Nao_Deve_Listar_Arquivo_Que_Nao_Usa_Mais_Cookie()
    {
        var root = RepoPaths.FindRepoRoot();
        var usam = ArquivosDaApiQueUsamCookie(root).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sobrando = Reincluidos().Where(rel => !usam.Contains(rel)).ToList();

        sobrando.Should().BeEmpty(
            "a lista de reinclusao do codeql.yml deve refletir quem usa cookie hoje. Entrada que " +
            "sobra esconde o erro de caminho ou de rename: o filtro nao reinclui nada e o arquivo " +
            "real fica sem analise.");
    }

    private static string[] LinhasDoWorkflow() =>
        File.ReadAllLines(Path.Combine(RepoPaths.FindRepoRoot(), WorkflowPath.Replace('/', Path.DirectorySeparatorChar)));

    private static HashSet<string> Reincluidos() =>
        LinhasDoWorkflow()
            .Select(l => ReinclusaoRegex.Match(l))
            .Where(m => m.Success)
            .Select(m => m.Groups["arquivo"].Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<string> ArquivosDaApiQueUsamCookie(string root)
    {
        var apiDir = Path.Combine(root, "EasyStock.Api");
        var sep = Path.DirectorySeparatorChar;

        foreach (var file in Directory.GetFiles(apiDir, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{sep}obj{sep}") || file.Contains($"{sep}bin{sep}")) continue;

            var rel = Path.GetRelativePath(root, file).Replace(sep, '/');
            if (InfraDoEsquema.Contains(rel)) continue;

            if (UsaCookieEmCodigo(File.ReadAllText(file)))
                yield return rel;
        }
    }

    /// <summary>Ignora comentario: doc citando o cookie nao autentica ninguem.</summary>
    private static bool UsaCookieEmCodigo(string conteudo)
    {
        foreach (var linha in conteudo.Split('\n'))
        {
            var t = linha.TrimStart();
            if (t.StartsWith("//") || t.StartsWith("*") || t.StartsWith("/*")) continue;
            if (UsoDeCookieRegex.IsMatch(linha)) return true;
        }
        return false;
    }
}

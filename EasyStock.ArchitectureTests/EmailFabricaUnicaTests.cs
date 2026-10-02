using System.Text.RegularExpressions;
using FluentAssertions;

namespace EasyStock.ArchitectureTests;

/// <summary>
/// Guard (N3, #1351): o <c>IEmailService</c> tem uma só fábrica, <c>AddEasyStockEmail</c>. Antes eram duas, com regras
/// diferentes: a da API escolhia por <c>Email:Provider</c> e, com o <c>Smtp:Host=smtp.gmail.com</c> do appsettings,
/// nunca caía no console; a do Worker só olhava se a seção <c>Smtp</c> existia e caía no console. Com o mesmo
/// <c>.env</c> os dois hosts se comportavam de forma oposta, e um deles mandava e-mail enquanto o outro fingia.
///
/// Source-text based (idioma de <see cref="RlsBypassAllowlistTests"/>), comentários ignorados. Detector novo: a prova
/// red-bar (injetar a violação, ver o teste falhar, reverter; ADR-0023, item 5) está na descrição da PR #1351.
/// </summary>
[Trait("Category", "Architecture")]
public class EmailFabricaUnicaTests
{
    private const string Fabrica = "EasyStock.Infra.Async/DependencyInjection/EmailServiceCollectionExtensions.cs";
    private const string RegistroDaApi = "EasyStock.Infra.Async/DependencyInjection/ServiceCollectionExtensions.cs";
    private const string ProgramDoWorker = "EasyStock.Worker/Program.cs";

    private static readonly string[] SufixosDeProjetoDeTeste =
        [".Tests", ".UnitTests", ".IntegrationTests", ".ArchitectureTests"];

    private static readonly Regex ConstroiSmtpEmailService = new(@"\bnew\s+(?:[\w.]+\.)?SmtpEmailService\s*\(", RegexOptions.Compiled);

    private static readonly Regex RegistraIEmailService = new(
        @"\b(?:Add|TryAdd)(?:Singleton|Scoped|Transient)\s*<\s*(?:[\w.]+\.)?IEmailService\b", RegexOptions.Compiled);

    private static readonly Regex ChamaAFabrica = new(@"\bAddEasyStockEmail\s*\(", RegexOptions.Compiled);

    [Fact]
    public void ApiEWorkerRegistramOEmailPelaMesmaExtensao()
    {
        var root = RepoPaths.FindRepoRoot();

        // A API registra pelo AddEasyStockAsyncInfrastructure (Program.cs da API), e o Worker chama a fábrica direto.
        CodigoSemComentarios(root, RegistroDaApi).Should().MatchRegex(ChamaAFabrica.ToString(),
            "a API monta o e-mail por AddEasyStockAsyncInfrastructure, que precisa chamar AddEasyStockEmail");
        CodigoSemComentarios(root, ProgramDoWorker).Should().MatchRegex(ChamaAFabrica.ToString(),
            "o Worker monta o e-mail pela mesma extensao da API, nao por uma fabrica propria");
        CodigoSemComentarios(root, "EasyStock.Api/Program.cs").Should().Contain("AddEasyStockAsyncInfrastructure(",
            "a API so tem e-mail se chamar AddEasyStockAsyncInfrastructure (ou a extensao direto)");
    }

    [Fact]
    public void SoAFabricaConstroiOSmtpEmailServiceERegistraOIEmailService()
    {
        var root = RepoPaths.FindRepoRoot();
        var constroemFora = new List<string>();
        var registramFora = new List<string>();

        foreach (var arquivo in ArquivosDeProducao(root))
        {
            var relativo = Path.GetRelativePath(root, arquivo).Replace(Path.DirectorySeparatorChar, '/');
            if (relativo.Equals(Fabrica, StringComparison.OrdinalIgnoreCase))
                continue;

            var codigo = SemComentarios(File.ReadAllText(arquivo));
            if (ConstroiSmtpEmailService.IsMatch(codigo))
                constroemFora.Add(relativo);
            if (RegistraIEmailService.IsMatch(codigo))
                registramFora.Add(relativo);
        }

        constroemFora.Should().BeEmpty(
            "new SmtpEmailService( so existe em {0}: um segundo lugar de construcao e a volta das duas fabricas "
            + "com regras diferentes (a API nunca cai no console, o Worker cai). Use AddEasyStockEmail.", Fabrica);
        registramFora.Should().BeEmpty(
            "IEmailService so e registrado por {0}; registrar em outro lugar faz API e Worker divergirem.", Fabrica);
    }

    [Fact]
    public void AFabricaExisteEConstroiOServicoSmtp()
    {
        var root = RepoPaths.FindRepoRoot();
        var caminho = Path.Combine(root, Fabrica.Replace('/', Path.DirectorySeparatorChar));

        // Falha explicita se o alvo sumiu: o teste precisa acompanhar o refactor em vez de virar verde-vazio.
        File.Exists(caminho).Should().BeTrue($"{Fabrica} sumiu: atualize este guard junto com a fabrica.");
        ConstroiSmtpEmailService.IsMatch(SemComentarios(File.ReadAllText(caminho))).Should().BeTrue(
            "a fabrica unica e o lugar que constroi o SmtpEmailService; se isso mudou, o guard acima deixou de proteger algo");
    }

    private static string CodigoSemComentarios(string root, string relativo)
    {
        var caminho = Path.Combine(root, relativo.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(caminho).Should().BeTrue($"{relativo} sumiu: atualize este guard.");
        return SemComentarios(File.ReadAllText(caminho));
    }

    private static string SemComentarios(string conteudo) =>
        string.Join('\n', conteudo.Split('\n').Where(linha =>
        {
            var t = linha.TrimStart();
            return !(t.StartsWith("//") || t.StartsWith("*") || t.StartsWith("/*"));
        }));

    private static IEnumerable<string> ArquivosDeProducao(string root) =>
        Directory.GetDirectories(root, "EasyStock.*")
            .Where(d => !SufixosDeProjetoDeTeste.Any(s => Path.GetFileName(d).EndsWith(s, StringComparison.Ordinal)))
            .SelectMany(d => Directory.GetFiles(d, "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                     && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)
                     && !f.Contains(Path.DirectorySeparatorChar + "node_modules" + Path.DirectorySeparatorChar));
}

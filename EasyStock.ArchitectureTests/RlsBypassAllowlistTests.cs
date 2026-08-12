using System.Text.RegularExpressions;
using FluentAssertions;

namespace EasyStock.ArchitectureTests;

/// <summary>
/// Guard (ADR-0010): o port <c>IRowLevelSecurityBypass</c> so pode ser tocado por quem esta
/// nesta allowlist.
///
/// Racionamento: a RLS do Postgres e a SEGUNDA camada de defesa do ADR-0010 — a que continua de
/// pe quando o filtro do EF falha. O bypass existe porque um punhado de operacoes e cross-tenant
/// por natureza (migrations, bootstrap, seed, jobs, webhooks sem JWT, login pre-auth e — desde a
/// issue #1024 — o registro de empresa, que CRIA o tenant). Fora dessas, ligar bypass nao
/// conserta nada: transforma um bug de contexto de tenant em vazamento silencioso entre clientes.
///
/// O port ja existia e ja tinha consumidores quando este teste foi escrito; o que faltava era o
/// portao. Injetar uma interface e barato demais para uma decisao deste peso — sem guarda, o
/// terceiro, o quarto e o decimo consumidor entram sem ninguem perceber, cada um por um motivo
/// que parecia bom no dia. Com a guarda, a porta continua existindo, mas nao da para atravessa-la
/// sem editar esta lista, e editar esta lista aparece no diff da PR.
///
/// <b>Antes de acrescentar um consumidor</b>, responda: a operacao cria ou atravessa tenants por
/// natureza, ou a query so voltou vazia? Se for o segundo caso, o defeito esta no contexto de
/// tenant, e o bypass so vai esconde-lo.
/// </summary>
[Trait("Category", "Architecture")]
public class RlsBypassAllowlistTests
{
    private static readonly Regex BypassPortRegex = new(
        @"\bIRowLevelSecurityBypass\b",
        RegexOptions.Compiled);

    /// <summary>
    /// Casa o port em CODIGO, ignorando linhas de comentario. Um <c>&lt;see cref&gt;</c> apontando
    /// para o port nao concede capacidade nenhuma — e so documentacao cruzada, e ha varias delas
    /// (INfeRepository, ReprocessarContingenciaCommand). Contar essas como violacao produziria
    /// falso positivo, e falso positivo em guarda de seguranca e pior que guarda nenhuma: treina
    /// quem mantem o codigo a engordar a allowlist ate calar o teste, e ai a lista para de
    /// significar "quem pode burlar RLS".
    /// </summary>
    private static bool ReferenciaOPortEmCodigo(string conteudo)
    {
        foreach (var linha in conteudo.Split('\n'))
        {
            var t = linha.TrimStart();
            if (t.StartsWith("//") || t.StartsWith("*") || t.StartsWith("/*")) continue;
            if (BypassPortRegex.IsMatch(linha)) return true;
        }
        return false;
    }

    /// <summary>
    /// Projetos varridos. Caminhos relativos a raiz do repo, forward slash. Projetos de teste
    /// ficam de fora: substitutes do port em teste nao burlam RLS nenhuma.
    /// </summary>
    private static readonly string[] ScannedProjectDirs =
    {
        "EasyStock.Domain",
        "EasyStock.Application",
        "EasyStock.Infra.Postgre",
        "EasyStock.Infra.Async",
        "EasyStock.Infra.Integrations",
        "EasyStock.Api",
        "EasyStock.Worker",
        "EasyStock.Web",
        "EasyStock.Admin",
    };

    /// <summary>
    /// Quem pode mencionar o port. Cada entrada e uma decisao consciente, nao um arquivo que
    /// "precisou". Caminhos relativos a raiz do repo, forward slash.
    /// </summary>
    private static readonly HashSet<string> Allowlist = new(StringComparer.OrdinalIgnoreCase)
    {
        // Definicao do port, implementacao e registro no container.
        "EasyStock.Application/Ports/Output/Security/IRowLevelSecurityBypass.cs",
        "EasyStock.Infra.Postgre/Security/RowLevelSecurityBypass.cs",
        "EasyStock.Infra.Postgre/DependencyInjection/ServiceCollectionExtensions.cs",

        // Webhook fiscal: chega sem JWT e o tenant e descoberto pelo payload, entao nao ha
        // app.empresa_id no momento em que a query precisa rodar.
        "EasyStock.Application/UseCases/Fiscal/ProcessarWebhookFocusNFe/ProcessarWebhookFocusNFeUseCase.cs",

        // Reprocessamento de contingencia (fix B-053): itera sobre multiplos tenants por design.
        "EasyStock.Application/UseCases/Fiscal/ReprocessarContingencia/ReprocessarContingenciaUseCase.cs",

        // issue #1024: registrar empresa e cross-tenant por definicao, porque cria o tenant. A
        // requisicao e anonima e nao existe app.empresa_id no contexto, entao a policy
        // tenant_isolation recusa os INSERTs (42501) e as leituras de perfis voltam vazias.
        "EasyStock.Application/UseCases/RegistrarEmpresa/RegistrarEmpresaUseCase.cs",
    };

    [Fact]
    public void Somente_Allowlist_Pode_Referenciar_O_Port_De_Bypass_De_RLS()
    {
        var root = RepoPaths.FindRepoRoot();
        var offenders = new List<string>();

        foreach (var dir in ScannedProjectDirs)
        {
            var fullDir = Path.Combine(root, dir);
            if (!Directory.Exists(fullDir)) continue;

            foreach (var file in Directory.GetFiles(fullDir, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)) continue;
                if (file.Contains(Path.DirectorySeparatorChar + ".nuget" + Path.DirectorySeparatorChar)) continue;

                var rel = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
                if (Allowlist.Contains(rel)) continue;

                if (ReferenciaOPortEmCodigo(File.ReadAllText(file)))
                    offenders.Add(rel);
            }
        }

        offenders.Should().BeEmpty(
            "IRowLevelSecurityBypass burla a segunda camada de defesa do ADR-0010 e so vale para " +
            "operacoes cross-tenant POR NATUREZA. Se a sua query voltou vazia, o defeito esta no " +
            "contexto de tenant, nao na RLS — bypass ali vira vazamento entre clientes. Se o caso " +
            "for legitimo mesmo, adicione o arquivo a Allowlist com o numero da issue e o porque, " +
            "para a decisao aparecer no diff da PR (refs #1024).");
    }

    [Fact]
    public void Allowlist_NaoDeveConterArquivosQueJaNaoUsamOPort()
    {
        var root = RepoPaths.FindRepoRoot();
        var stale = new List<string>();

        foreach (var entry in Allowlist)
        {
            var fullPath = Path.Combine(root, entry.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
                stale.Add($"{entry} (arquivo nao existe)");
            else if (!ReferenciaOPortEmCodigo(File.ReadAllText(fullPath)))
                stale.Add($"{entry} (nao referencia mais o port; remova da Allowlist)");
        }

        stale.Should().BeEmpty(
            "A Allowlist deve refletir o estado real. Entrada que sobra e permissao concedida a " +
            "quem nao pediu — o proximo arquivo a ocupar aquele caminho herdaria o bypass de graca.");
    }
}

using System.Text.RegularExpressions;
using FluentAssertions;

namespace EasyStock.ArchitectureTests;

/// <summary>
/// Guard (Anel 1 / ADR-0032): DateTime.Now, DateTime.Today, DateTimeOffset.Now e
/// TimeZoneInfo.Local NAO podem aparecer nos projetos core do backend.
///
/// Racionamento: o servidor roda em UTC; esses membros retornam o "horario local do
/// servidor", que em producao e UTC — nao Brasilia. O codigo correto usa:
///   - HorarioBrasil.Hoje() / JanelaDiaUtc() / HojeInstanteUtc() (Application.Common)
///   - BrazilTime.Today() / ParaBrasilia()    (Web/Admin helpers)
///   - TimeProvider.GetUtcNow()               (injetavel, testavel)
///
/// Ratchet: DebtAllowlist contem os 3 arquivos pre-existentes. Ao corrigir, remova
/// a entrada (o teste falha se o arquivo ainda estiver na lista mas o padrao sumiu,
/// forcando a limpeza). O debito so diminui; qualquer ocorrencia nova e barrada.
///
/// Exclusoes justificadas de escopo: EasyStok.Mobile (app nativo, relogio do dispositivo
/// e correto), EasyStock.Web/Admin (BFF/display — BrazilTime ja foi aplicado), projetos
/// *.Tests/*.UnitTests/*.IntegrationTests (fixtures podem usar datas fixas).
/// </summary>
[Trait("Category", "Architecture")]
public class AmbientClockBanTests
{
    private static readonly Regex AmbientClockRegex = new(
        @"\bDateTime\.(Now|Today)\b|\bDateTimeOffset\.Now\b|\bTimeZoneInfo\.Local\b",
        RegexOptions.Compiled);

    /// <summary>
    /// Projetos core varridos. Caminhos relativos a raiz do repo, forward slash.
    /// NAO inclui Mobile, Web, Admin, projetos de teste.
    /// </summary>
    private static readonly string[] CoreProjectDirs =
    {
        "EasyStock.Domain",
        "EasyStock.Application",
        "EasyStock.Infra.Postgre",
        "EasyStock.Infra.Async",
        "EasyStock.Infra.Integrations",
        "EasyStock.Api",
        "EasyStock.Worker",
    };

    /// <summary>
    /// Debito pre-existente tolerado — cada fase do plano de correcao remove sua entrada.
    /// Caminhos relativos a raiz do repo, forward slash.
    ///
    /// Fase B (validade): remove EasyStock.Domain/Specifications/ItemEstoqueProximoDoVencimentoSpecification.cs
    /// Fase D (fiscal):   remove EasyStock.Infra.Integrations/Fiscal/FocusNFe/NfeCertificadoA1Service.cs
    /// Baixa prio:        remove EasyStock.Infra.Async/Reporting/Handlers/EstoquePosicaoAtualHandler.cs
    /// </summary>
    private static readonly HashSet<string> DebtAllowlist = new(StringComparer.OrdinalIgnoreCase)
    {
        // Fase B: DateTime.Today em ItemEstoqueProximoDoVencimentoSpecification (fix = HorarioBrasil.Hoje())
        "EasyStock.Domain/Specifications/ItemEstoqueProximoDoVencimentoSpecification.cs",
        // Fase D: DateTime.Now em NfeCertificadoA1Service (X509.NotAfter Kind=Local — fix = normalizar para UTC)
        "EasyStock.Infra.Integrations/Fiscal/FocusNFe/NfeCertificadoA1Service.cs",
        // Baixa prio: DateTime.Today em nome de arquivo de relatorio (nao afeta dados)
        "EasyStock.Infra.Async/Reporting/Handlers/EstoquePosicaoAtualHandler.cs",
    };

    [Fact]
    public void Core_NaoDeveUsar_RelogioAmbiente_ForaDaAllowlist()
    {
        var root = RepoPaths.FindRepoRoot();
        var offenders = new List<string>();

        foreach (var dir in CoreProjectDirs)
        {
            var fullDir = Path.Combine(root, dir);
            if (!Directory.Exists(fullDir)) continue;

            foreach (var file in Directory.GetFiles(fullDir, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)) continue;
                var rel = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
                if (DebtAllowlist.Contains(rel)) continue;

                if (AmbientClockRegex.IsMatch(File.ReadAllText(file)))
                    offenders.Add(rel);
            }
        }

        offenders.Should().BeEmpty(
            "DateTime.Now/Today, DateTimeOffset.Now e TimeZoneInfo.Local nao podem aparecer " +
            "nos projetos core. Use HorarioBrasil.Hoje()/JanelaDiaUtc()/HojeInstanteUtc() " +
            "(Application.Common) ou TimeProvider.GetUtcNow(). Se for debito transitorio, " +
            "adicione o arquivo a DebtAllowlist com comentario da fase/issue de correcao (refs #553).");
    }

    [Fact]
    public void DebtAllowlist_NaoDeveConterArquivosJaLimpos()
    {
        var root = RepoPaths.FindRepoRoot();
        var stale = new List<string>();

        foreach (var entry in DebtAllowlist)
        {
            var fullPath = Path.Combine(root, entry.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
                stale.Add($"{entry} (arquivo nao existe)");
            else if (!AmbientClockRegex.IsMatch(File.ReadAllText(fullPath)))
                stale.Add($"{entry} (padrao removido - limpo; remova da DebtAllowlist)");
        }

        stale.Should().BeEmpty(
            "DebtAllowlist deve refletir o estado real do debito. " +
            "Remova entradas ja limpas ou cujo arquivo nao existe mais (o debito so diminui).");
    }
}

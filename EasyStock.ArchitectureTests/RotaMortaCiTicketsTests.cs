using System.Text.RegularExpressions;
using FluentAssertions;

namespace EasyStock.ArchitectureTests;

/// <summary>
/// Guard (N10, #1375): nenhum codigo de producao chama <c>/api/ci/tickets</c>. A rota foi apagada em <c>6b0a6f98</c>
/// (<c>AutoTicketController</c>) e o <c>EndpointHealthMonitorService</c> ainda fazia POST nela, sem efeito, so se
/// <c>Ci:AutoTicketKey</c> existisse. O aviso de problema agora sai por <c>IPublicadorIncidenteSistema</c>.
///
/// Varre os <c>.cs</c> dos projetos core pelo literal <c>"/api/ci/tickets"</c> entre aspas; comentario nao conta.
/// Detector novo: a prova red-bar (ADR-0023, item 5) esta na descricao da PR: com o POST antigo o teste falhava em
/// <c>EndpointHealthMonitorService.cs</c>.
/// </summary>
[Trait("Category", "Architecture")]
public class RotaMortaCiTicketsTests
{
    private static readonly string[] ProjetosCore =
    [
        "EasyStock.Domain", "EasyStock.Application", "EasyStock.Infra.Postgre", "EasyStock.Infra.Async",
        "EasyStock.Infra.Integrations", "EasyStock.Api", "EasyStock.Worker",
    ];

    private static readonly Regex LiteralDaRota = new("\"[^\"\\r\\n]*/api/ci/tickets[^\"\\r\\n]*\"", RegexOptions.Compiled);

    [Fact]
    public void NenhumCodigoPostaParaApiCiTickets()
    {
        var root = RepoPaths.FindRepoRoot();
        var achados = new List<string>();

        foreach (var projeto in ProjetosCore)
        {
            var dir = Path.Combine(root, projeto);
            if (!Directory.Exists(dir)) continue;

            foreach (var arquivo in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                var separador = Path.DirectorySeparatorChar;
                if (arquivo.Contains($"{separador}obj{separador}") || arquivo.Contains($"{separador}bin{separador}")) continue;

                var codigo = string.Join('\n', File.ReadAllLines(arquivo).Where(l =>
                {
                    var t = l.TrimStart();
                    return !(t.StartsWith("//") || t.StartsWith("*") || t.StartsWith("/*"));
                }));
                if (LiteralDaRota.IsMatch(codigo))
                    achados.Add(Path.GetRelativePath(root, arquivo).Replace(separador, '/'));
            }
        }

        achados.Should().BeEmpty(
            "a rota /api/ci/tickets nao existe mais; avisar problema e papel do IPublicadorIncidenteSistema.");
    }
}

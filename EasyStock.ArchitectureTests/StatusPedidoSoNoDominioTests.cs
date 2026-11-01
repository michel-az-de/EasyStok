using System.Text.RegularExpressions;
using FluentAssertions;

namespace EasyStock.ArchitectureTests;

/// <summary>
/// Guard (#2174, spec S3 de docs/plan/qualidade-backend): o status do Pedido so muda dentro do
/// EasyStock.Domain, por <c>Pedido.Criar</c>, <c>MudarStatus</c>, <c>Cancelar</c> ou as compensacoes
/// nomeadas do agregado. Atribuir <c>Status = StatusPedidoMapper.X</c> fora dele pula a
/// <c>PedidoStateMachine</c> e os carimbos de data (CanceladoEm, EntreguEm).
///
/// Escopo: projetos core do backend, menos o Domain. Seeders de demonstracao e o importador
/// legado do Mobile gravam literal e ficam fora do padrao varrido.
/// </summary>
[Trait("Category", "Architecture")]
public class StatusPedidoSoNoDominioTests
{
    private static readonly Regex AtribuicaoDeStatus = new(
        @"\bStatus\s*=\s*StatusPedidoMapper\.",
        RegexOptions.Compiled);

    private static readonly string[] ProjetosForaDoDominio =
    {
        "EasyStock.Application",
        "EasyStock.Infra.Postgre",
        "EasyStock.Infra.Async",
        "EasyStock.Infra.Integrations",
        "EasyStock.Infra.Notifications",
        "EasyStock.Api",
        "EasyStock.Worker",
    };

    [Fact]
    public void StatusDoPedido_SoMuda_DentroDoDominio()
    {
        var root = RepoPaths.FindRepoRoot();
        var offenders = new List<string>();

        foreach (var dir in ProjetosForaDoDominio)
        {
            var fullDir = Path.Combine(root, dir);
            if (!Directory.Exists(fullDir)) continue;

            foreach (var file in Directory.GetFiles(fullDir, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)) continue;
                if (file.Contains(Path.DirectorySeparatorChar + ".nuget" + Path.DirectorySeparatorChar)) continue;

                var linhas = File.ReadAllLines(file);
                for (var i = 0; i < linhas.Length; i++)
                {
                    if (AtribuicaoDeStatus.IsMatch(linhas[i]))
                        offenders.Add($"{Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/')}:{i + 1}");
                }
            }
        }

        offenders.Should().BeEmpty(
            "o status do Pedido muda so no Domain. Use Pedido.Criar(statusInicial:), MudarStatus, Cancelar " +
            "ou a compensacao nomeada do agregado, que validam a transicao na PedidoStateMachine (#2174).");
    }
}

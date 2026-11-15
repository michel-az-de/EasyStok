using EasyStock.Domain.Entities;
using FluentAssertions;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace EasyStock.ArchitectureTests;

/// <summary>
/// Guard (#2171, spec S3 de docs/plan/qualidade-backend): nenhum assembly do backend fora do
/// EasyStock.Domain chama o setter de <see cref="Pedido.Status"/>. Le o IL (Mono.Cecil), entao pega
/// qualquer forma de atribuicao: literal, variavel, inicializador de objeto ou constante do mapper.
/// A trava por texto (<see cref="StatusPedidoSoNoDominioTests"/>) so via
/// <c>Status = StatusPedidoMapper.X</c> e deixava passar <c>pedido.Status = "entregue"</c>.
///
/// A allowlist e a da spec: seeders de demonstracao e o importador legado do Mobile. Ela so diminui.
/// O EasyStock.Worker nao esta no diretorio de saida deste projeto e segue coberto pela trava por texto.
/// </summary>
[Trait("Category", "Architecture")]
public class StatusPedidoSoNoDominioIlTests
{
    private static readonly HashSet<string> Allowlist = new(StringComparer.Ordinal)
    {
        "EasyStock.Api.Data.DemoStore.LogisticaDemoSeeder",
        "EasyStock.Api.Mobile.Services.LegacySnapshotImportService",
    };

    [Fact]
    public void SetterDoStatusDoPedido_SoEChamadoDentroDoDominio()
    {
        var setter = typeof(Pedido).GetProperty(nameof(Pedido.Status))!.SetMethod!;
        var alvoTipo = setter.DeclaringType!.FullName!;
        var dominio = typeof(Pedido).Assembly.GetName().Name!;

        var assemblies = Directory.GetFiles(AppContext.BaseDirectory, "EasyStock.*.dll")
            .Where(p => !Path.GetFileNameWithoutExtension(p).Equals(dominio, StringComparison.Ordinal))
            .Where(p => !Path.GetFileNameWithoutExtension(p).Contains("Tests", StringComparison.Ordinal))
            .ToList();
        assemblies.Should().Contain(p => p.EndsWith("EasyStock.Api.dll", StringComparison.Ordinal)
            || p.EndsWith("EasyStock.Application.dll", StringComparison.Ordinal),
            "a varredura precisa enxergar ao menos Api e Application no diretorio de saida");

        var chamadores = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var caminho in assemblies)
        {
            using var modulo = ModuleDefinition.ReadModule(caminho);
            foreach (var tipo in modulo.GetTypes())
            foreach (var metodo in tipo.Methods.Where(m => m.HasBody))
            foreach (var instr in metodo.Body.Instructions)
            {
                if (instr.OpCode != OpCodes.Call && instr.OpCode != OpCodes.Callvirt) continue;
                if (instr.Operand is not MethodReference mr) continue;
                if (mr.Name != setter.Name || mr.DeclaringType.FullName != alvoTipo) continue;
                chamadores.Add(TipoDeOrigem(tipo));
            }
        }

        chamadores.Except(Allowlist).Should().BeEmpty(
            "o status do Pedido muda so no Domain. Use Pedido.Criar(statusInicial:), MudarStatus, Cancelar " +
            "ou a compensacao nomeada do agregado, que validam a transicao na PedidoStateMachine (#2171).");
        Allowlist.Except(chamadores).Should().BeEmpty(
            "a allowlist so diminui: remova dela o tipo que deixou de gravar o status cru");
    }

    /// <summary>Sobe de lambdas e maquinas de estado async ate o tipo escrito no codigo.</summary>
    private static string TipoDeOrigem(TypeDefinition tipo)
    {
        var atual = tipo;
        while (atual.IsNested && atual.Name.StartsWith('<')) atual = atual.DeclaringType;
        return atual.FullName.Replace('/', '+');
    }
}

using System.Reflection;
using FluentAssertions;

namespace EasyStock.ArchitectureTests;

/// <summary>
/// #825: o gate pré-commit (scripts/poka-yoke/gate.ps1) roda só <c>Category=Architecture</c> e o CI só exclui
/// <c>ArchitectureDebt</c>. Teste deste projeto sem uma das duas categorias roda no CI e nunca no gate local:
/// quebrar "Domain não depende de Infra" passava no commit e só caía no CI. Este meta-teste trava a regressão.
/// </summary>
[Trait("Category", "Architecture")]
public class TodoTesteDeArquiteturaTemCategoriaTests
{
    private static readonly string[] CategoriasAceitas = ["Architecture", "ArchitectureDebt"];

    [Fact]
    public void Todo_teste_do_projeto_tem_categoria_Architecture_ou_ArchitectureDebt()
    {
        var semCategoria = typeof(TodoTesteDeArquiteturaTemCategoriaTests).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes<FactAttribute>(inherit: true).Any())
                .Where(m => !Categorias(m).Concat(Categorias(t)).Any(c => CategoriasAceitas.Contains(c)))
                .Select(m => $"{t.FullName}.{m.Name}"))
            .Order()
            .ToList();

        semCategoria.Should().BeEmpty(
            "o gate local roda só Category=Architecture; teste sem categoria só roda no CI. Marque a classe com " +
            "[Trait(\"Category\", \"Architecture\")] (ou ArchitectureDebt se a regra ainda está vermelha)");
    }

    private static IEnumerable<string> Categorias(MemberInfo membro) =>
        membro.GetCustomAttributesData()
            .Where(a => a.AttributeType == typeof(TraitAttribute)
                        && a.ConstructorArguments.Count == 2
                        && (string?)a.ConstructorArguments[0].Value == "Category")
            .Select(a => (string)a.ConstructorArguments[1].Value!);
}

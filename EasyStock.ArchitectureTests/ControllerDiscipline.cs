using NetArchTest.Rules;
using FluentAssertions;

namespace EasyStock.ArchitectureTests;

/// <summary>
/// Trava regressão F7 (DbContext direto em controllers Admin).
///
/// Hoje 22 controllers da Api injetam EasyStockDbContext diretamente, anti-pattern
/// que esta fase do refactor pretende eliminar. Enquanto F7 não fecha, este teste
/// fica em <c>ArchitectureDebt</c> (não bloqueia Husky pre-commit). Quando todos os
/// 22 controllers migrarem pra UseCases, troca o Trait pra <c>Architecture</c> e
/// vira gate permanente.
/// </summary>
public class ControllerDiscipline
{
    [Fact]
    [Trait("Category", "ArchitectureDebt")]
    public void Controllers_NaoDevem_DependerDe_EasyStockDbContext()
    {
        // Arrange
        var apiAssembly = typeof(EasyStock.Api.Http.EasyStockControllerBase).Assembly;

        // Act
        // Pega todas as classes terminadas em "Controller" no projeto Api e verifica
        // que nenhuma tem dependência em namespaces do DbContext concreto.
        var result = Types.InAssembly(apiAssembly)
            .That()
            .HaveNameEndingWith("Controller")
            .ShouldNot()
            .HaveDependencyOnAny(
                "EasyStock.Infra.Postgre.Data",
                "EasyStock.Infra.Postgre.Data.EasyStockDbContext")
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(
            "Controllers devem consumir UseCases/Repositories (ports da Application); " +
            "DbContext concreto vive em Infra.Postgre. Quando F7 fechar, esta regra " +
            "vira Category=Architecture (gate permanente). Hoje listada como ArchitectureDebt." +
            (result.FailingTypeNames is null
                ? string.Empty
                : $" Tipos atualmente em violação: {string.Join(", ", result.FailingTypeNames)}"));
    }
}

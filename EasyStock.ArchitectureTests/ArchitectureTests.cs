using NetArchTest.Rules;
using FluentAssertions;

namespace EasyStock.ArchitectureTests;

public class ArchitectureTests
{
    private const string DomainNamespace = "EasyStock.Domain";
    private const string ApplicationNamespace = "EasyStock.Application";
    private const string InfraNamespace = "EasyStock.Infra";
    private const string ApiNamespace = "EasyStock.Api";

    [Fact]
    public void Domain_Nao_Deve_Depender_De_Application_Infrastructure_Ou_Api()
    {
        // Arrange
        var assembly = typeof(EasyStock.Domain.Entities.Produto).Assembly;

        // Act
        var result = Types.InAssembly(assembly)
            .ShouldNot()
            .HaveDependencyOnAny(ApplicationNamespace, InfraNamespace, ApiNamespace)
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Application_Nao_Deve_Depender_De_Infrastructure_Ou_Api()
    {
        // Arrange
        var assembly = typeof(EasyStock.Application.Ports.Output.Persistence.IProdutoRepository).Assembly;

        // Act
        var result = Types.InAssembly(assembly)
            .ShouldNot()
            .HaveDependencyOnAny(InfraNamespace, ApiNamespace)
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Infrastructure_Pode_Depender_De_Domain_E_Application()
    {
        // Arrange
        var assembly = typeof(EasyStock.Infra.Postgre.Data.EasyStockDbContext).Assembly;

        // Assert
        var referencedAssemblies = assembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();

        referencedAssemblies.Should().Contain(name => name!.StartsWith(DomainNamespace));
        referencedAssemblies.Should().Contain(name => name!.StartsWith(ApplicationNamespace));
    }

    [Fact]
    public void Repositories_Devem_Ficar_Fora_Do_Domain()
    {
        // Arrange
        var assembly = typeof(EasyStock.Domain.Entities.Produto).Assembly;

        // Act
        var result = Types.InAssembly(assembly)
            .ShouldNot()
            .HaveNameEndingWith("Repository")
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void ValueObjects_Devem_Ficar_No_Domain()
    {
        var valueObjects = new[]
        {
            typeof(EasyStock.Domain.ValueObjects.Dinheiro),
            typeof(EasyStock.Domain.ValueObjects.Quantidade),
            typeof(EasyStock.Domain.ValueObjects.Validade),
            typeof(EasyStock.Domain.ValueObjects.CodigoSku),
            typeof(EasyStock.Domain.ValueObjects.CodigoLote),
            typeof(EasyStock.Domain.ValueObjects.Dimensoes)
        };

        valueObjects.Should().OnlyContain(type => type.Namespace == DomainNamespace + ".ValueObjects");
    }

    [Fact]
    public void Exceptions_De_Domain_Devem_Ficar_No_Domain()
    {
        // Arrange
        var assembly = typeof(EasyStock.Domain.Exceptions.RegraDeDominioVioladaException).Assembly;

        // Act
        var result = Types.InAssembly(assembly)
            .That()
            .HaveNameEndingWith("Exception")
            .Should()
            .ResideInNamespace(DomainNamespace + ".Exceptions")
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Services_De_Domain_Devem_Ficar_No_Domain()
    {
        // Arrange
        var assembly = typeof(EasyStock.Domain.Services.CalculadoraReposicaoEstoque).Assembly;

        // Act
        var result = Types.InAssembly(assembly)
            .That()
            .HaveNameEndingWith("CalculadoraReposicaoEstoque")
            .Or()
            .HaveNameEndingWith("AnalisadorSaudeEstoque")
            .Or()
            .HaveNameEndingWith("PoliticaValidadeEstoque")
            .Or()
            .HaveNameEndingWith("CalculadoraResumoVenda")
            .Should()
            .ResideInNamespace(DomainNamespace + ".Services")
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue();
    }
}

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

    // ── Regras adicionais de pureza da camada Application ─────────────────
    // Application e POCO: deve depender apenas de abstracoes (Logging.Abstractions,
    // DI.Abstractions, IOptions, ports proprios) e das suas portas. Implementacoes
    // concretas de cache em-memoria, EF Core, ASP.NET Core e bibliotecas de hashing
    // entram aqui via Infra. As regras abaixo evitam regressao para o estado pre-auditoria
    // de 2026-05-08, que tinha BCrypt e IMemoryCache vazando para use cases.

    [Fact]
    public void Application_Nao_Deve_Depender_De_EntityFrameworkCore()
    {
        // Arrange
        var assembly = typeof(EasyStock.Application.Ports.Output.Persistence.IProdutoRepository).Assembly;

        // Act
        var result = Types.InAssembly(assembly)
            .ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Npgsql", "MongoDB.Driver")
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(
            "Application deve falar com persistencia atraves de ports — EF/Npgsql/MongoDriver vivem em Infra.*");
    }

    [Fact]
    public void Application_Nao_Deve_Depender_De_AspNetCore()
    {
        // Arrange
        var assembly = typeof(EasyStock.Application.Ports.Output.Persistence.IProdutoRepository).Assembly;

        // Act
        var result = Types.InAssembly(assembly)
            .ShouldNot()
            .HaveDependencyOnAny("Microsoft.AspNetCore")
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(
            "Application e POCO — HttpContext, IActionResult, controllers vivem em EasyStock.Api.");
    }

    [Fact]
    public void Application_Nao_Deve_Depender_De_BCrypt()
    {
        // Arrange. Hashing de senha consumido via IPasswordHasher; BCrypt e detalhe
        // de implementacao em EasyStock.Infra.Async (BCryptPasswordHasher).
        var assembly = typeof(EasyStock.Application.Ports.Output.Persistence.IProdutoRepository).Assembly;

        // Act
        var result = Types.InAssembly(assembly)
            .ShouldNot()
            .HaveDependencyOnAny("BCrypt.Net")
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(
            "Use cases consomem IPasswordHasher; BCrypt fica em Infra.Async.");
    }

    [Fact]
    public void Application_Nao_Deve_Depender_De_IMemoryCache()
    {
        // Arrange. Cache via ICacheService (port em Application), implementado por
        // RedisCacheService/InMemoryCacheService em Infra.Async. IMemoryCache concreto
        // acopla a aplicacao a um backend especifico.
        var assembly = typeof(EasyStock.Application.Ports.Output.Persistence.IProdutoRepository).Assembly;

        // Act
        var result = Types.InAssembly(assembly)
            .ShouldNot()
            .HaveDependencyOnAny("Microsoft.Extensions.Caching.Memory")
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(
            "Use cases devem usar ICacheService — IMemoryCache (concreto) vive em Infra.Async.");
    }
}

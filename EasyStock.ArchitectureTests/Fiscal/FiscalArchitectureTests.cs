using NetArchTest.Rules;
using FluentAssertions;

namespace EasyStock.ArchitectureTests.Fiscal;

public class FiscalArchitectureTests
{
    [Fact]
    public void Entidades_de_dominio_Fiscal_devem_usar_prefixo_Nfe_ou_Empresa()
    {
        var prefixosPermitidos = new[] { "Nfe", "Empresa", "Regime", "Status", "Ambiente" };

        var entidades = Types.InAssembly(typeof(EasyStock.Domain.Fiscal.NfeDocumento).Assembly)
            .That().ResideInNamespace("EasyStock.Domain.Fiscal")
            .GetTypes();

        foreach (var t in entidades)
        {
            var temPrefixoValido = prefixosPermitidos.Any(p => t.Name.StartsWith(p));
            temPrefixoValido.Should().BeTrue(
                $"tipo {t.Name} em EasyStock.Domain.Fiscal deve comecar com Nfe/Empresa/Regime/Status/Ambiente (ADR-0018). " +
                "Se for um conceito novo, atualize esta lista ou o ADR.");
        }
    }

    [Fact]
    public void UseCases_Fiscal_nao_devem_depender_de_EntityFrameworkCore()
    {
        var result = Types.InAssembly(typeof(EasyStock.Application.Ports.Output.Persistence.IProdutoRepository).Assembly)
            .That().ResideInNamespaceContaining("Fiscal")
            .ShouldNot()
            .HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "use cases fiscais nao devem depender de EF Core diretamente — acesso via ports/interfaces");
    }

    [Fact]
    public void UseCases_Fiscal_nao_devem_depender_de_IConfiguration()
    {
        var result = Types.InAssembly(typeof(EasyStock.Application.Ports.Output.Persistence.IProdutoRepository).Assembly)
            .That().ResideInNamespaceContaining("Fiscal")
            .ShouldNot()
            .HaveDependencyOn("Microsoft.Extensions.Configuration")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "use cases fiscais nao devem ler config diretamente — receber via construtor ou Options");
    }
}

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
}

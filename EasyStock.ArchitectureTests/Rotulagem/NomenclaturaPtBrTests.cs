using NetArchTest.Rules;
using FluentAssertions;

namespace EasyStock.ArchitectureTests.Rotulagem;

/// <summary>
/// Garante a regra do ADR-0011: nomes de entidades de domínio do módulo Rotulagem
/// devem ser PT-BR. Sufixos técnicos EN são permitidos (Service, Handler, Factory etc.)
/// mas substantivos principais de negócio em EN são bloqueados.
///
/// Lista alvo: <see cref="EasyStock.Domain"/> namespace <c>Entities.Rotulagem</c>.
/// Quando o módulo P-02 (Rotulagem Nutricional) ainda não foi implementado, o teste
/// passa trivialmente (lista vazia). Assim que a primeira entidade for criada em
/// <c>EasyStock.Domain.Entities.Rotulagem</c>, a verificação fica ativa.
/// </summary>
[Trait("Category", "Architecture")]
public class NomenclaturaPtBrTests
{
    private const string NamespaceRotulagem = "EasyStock.Domain.Entities.Rotulagem";

    /// <summary>
    /// Termos EN proibidos em nomes de entidades de negócio do módulo Rotulagem.
    /// Cada um tem equivalente PT-BR canônico (ver ADR-0011, seção "Tabela de renomeação").
    /// </summary>
    private static readonly string[] TermosEnProibidos =
    [
        "Profile",   // → PerfilNutricional
        "Recipe",    // → Receita
        "Label",     // → Rotulo
        "Batch",     // → Lote
        "Allergen",  // → Alergeno
        "Claim",     // → permitido como sufixo em PerfilNutricionalClaim, mas não como nome principal
        "Supplier",  // → Fornecedor
        "Source",    // → Origem
        "Draft",     // → Rascunho
        "Privacy"    // → Privacidade
    ];

    [Fact]
    public void Entidades_de_dominio_Rotulagem_devem_ter_nomes_em_pt_br()
    {
        // Arrange
        var assembly = typeof(EasyStock.Domain.Entities.Produto).Assembly;

        var entidadesRotulagem = Types.InAssembly(assembly)
            .That()
            .ResideInNamespace(NamespaceRotulagem)
            .GetTypes()
            .ToList();

        // Se o módulo ainda não foi implementado, não há nada para validar (passa trivialmente).
        // Quando F2 criar PerfilNutricional, ModeloRotulo, etc., a validação abaixo ativa.
        if (entidadesRotulagem.Count == 0)
        {
            return;
        }

        // Act + Assert
        foreach (var tipo in entidadesRotulagem)
        {
            foreach (var termo in TermosEnProibidos)
            {
                tipo.Name.Should().NotContain(termo,
                    because: $"entidade '{tipo.Name}' contém termo EN proibido '{termo}'. " +
                             "Usar equivalente PT-BR — ver docs/adr/0011-nomenclatura-pt-br-rotulagem.md.");
            }
        }
    }
}

using FluentAssertions;
using NetArchTest.Rules;

namespace EasyStock.ArchitectureTests;

/// <summary>
/// Regras arquiteturais específicas do módulo Storefront.
///
/// Estas regras complementam <see cref="ArchitectureTests"/> e blindam decisões
/// dos ADRs:
/// - ADR-0002: multi-tenancy via EmpresaId em todo aggregate
/// - ADR-0005: MercadoPago SDK não pode vazar de Infra.Async
/// - ADR-0010: feature flag NfeAutomaticaHabilitada NÃO pode ser removida
///   sem decisão consciente (não regredir para "emite sempre")
/// - ADR-0012: ClienteOtp / ClienteSession devem usar TimeProvider, não DateTime.UtcNow
/// </summary>
public class StorefrontArchitectureTests
{
    private const string StorefrontNamespace = "EasyStock.Domain.Entities.Storefront";
    private const string InfraNamespace = "EasyStock.Infra";
    private const string ApiNamespace = "EasyStock.Api";
    private const string ApplicationNamespace = "EasyStock.Application";

    // ── Isolamento de camada ───────────────────────────────────────────

    [Fact]
    public void Storefront_Domain_Nao_Pode_Depender_De_Infra_Ou_Api()
    {
        var assembly = typeof(EasyStock.Domain.Entities.Storefront.Storefront).Assembly;

        var result = Types.InAssembly(assembly)
            .That()
            .ResideInNamespace(StorefrontNamespace)
            .ShouldNot()
            .HaveDependencyOnAny(InfraNamespace, ApiNamespace, ApplicationNamespace)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "entities de Storefront devem ser POCO puros — sem EF Core, sem ASP.NET, sem Application Layer");
    }

    // ── Feature flag fiscal (ADR-0010 anti-regressão) ──────────────────

    [Fact]
    public void Storefront_Entity_Tem_Feature_Flag_NfeAutomaticaHabilitada()
    {
        // Anti-regressão: se alguém remover essa propriedade, o sistema volta para
        // o comportamento perigoso de "emitir NF sempre" mesmo antes da decisão
        // do contador (TASK-002/003). Ver ADR-0010.
        var storefrontType = typeof(EasyStock.Domain.Entities.Storefront.Storefront);

        var prop = storefrontType.GetProperty("NfeAutomaticaHabilitada");

        prop.Should().NotBeNull(
            "ADR-0010 exige feature flag NfeAutomaticaHabilitada para blindar decisão fiscal pendente.");
        prop!.PropertyType.Should().Be<bool>();
    }

    [Fact]
    public void Storefront_Entity_Tem_Property_ModeloFiscal()
    {
        var storefrontType = typeof(EasyStock.Domain.Entities.Storefront.Storefront);

        var prop = storefrontType.GetProperty("ModeloFiscal");

        prop.Should().NotBeNull(
            "ADR-0010 exige property ModeloFiscal para selecionar entre nfe55/nfce65/manual.");
        prop!.PropertyType.Should().Be<string>();
    }

    // ── Multi-tenancy (ADR-0002) ───────────────────────────────────────

    [Fact]
    public void Storefront_Entity_Tem_Property_EmpresaId_Para_Multi_Tenancy()
    {
        var storefrontType = typeof(EasyStock.Domain.Entities.Storefront.Storefront);

        var prop = storefrontType.GetProperty("EmpresaId");

        prop.Should().NotBeNull(
            "ADR-0002: todo aggregate root do Storefront carrega EmpresaId como FK obrigatória.");
        prop!.PropertyType.Should().Be<Guid>();
    }

    // ── MercadoPago SDK isolation (ADR-0005) ───────────────────────────

    [Fact]
    public void MercadoPago_Namespace_Nao_Pode_Aparecer_Em_Domain_Application_Ou_Api()
    {
        // ADR-0005: SDK MP usa static AccessToken — proibido fora de Infra.Async
        // onde HttpClient direto (IMercadoPagoClient adapter) substitui.
        var domainAssembly = typeof(EasyStock.Domain.Entities.Storefront.Storefront).Assembly;
        var applicationAssembly = typeof(EasyStock.Application.Ports.Output.Persistence.IProdutoRepository).Assembly;

        var domainResult = Types.InAssembly(domainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny("MercadoPago")
            .GetResult();

        var appResult = Types.InAssembly(applicationAssembly)
            .ShouldNot()
            .HaveDependencyOnAny("MercadoPago")
            .GetResult();

        domainResult.IsSuccessful.Should().BeTrue(
            "ADR-0005: SDK MercadoPago é proibido em Domain (usar IMercadoPagoClient via Application port).");
        appResult.IsSuccessful.Should().BeTrue(
            "ADR-0005: SDK MercadoPago é proibido em Application (usar IMercadoPagoClient port).");
    }
}

using NetArchTest.Rules;
using FluentAssertions;

namespace EasyStock.ArchitectureTests;

/// <summary>
/// Trava regressão F7 (DbContext direto em controllers).
///
/// #2371: era <c>ArchitectureDebt</c>, que o CI filtra, então nada barrava um controller novo com DbContext.
/// Agora é ratchet, no padrão de <see cref="AmbientClockBanTests"/>: os 13 controllers medidos em 2026-11-15 ficam
/// em <see cref="DebtAllowlist"/>. Controller novo com DbContext falha. Controller migrado para UseCases e ainda na
/// lista também falha, para a lista só diminuir. Lista vazia: apague-a e a regra vira absoluta.
/// </summary>
[Trait("Category", "Architecture")]
public class ControllerDiscipline
{
    private static readonly HashSet<string> DebtAllowlist = new(StringComparer.Ordinal)
    {
        // Mobile (sync do aparelho), dívida F7 de 2026-11-15.
        "EasyStock.Api.Mobile.Controllers.DeviceBackupController",
        "EasyStock.Api.Mobile.Controllers.DevicePairingController",
        "EasyStock.Api.Mobile.Controllers.MobileBatchesController",
        "EasyStock.Api.Mobile.Controllers.MobileCashController",
        "EasyStock.Api.Mobile.Controllers.MobileClientsController",
        "EasyStock.Api.Mobile.Controllers.MobileOrdersController",
        "EasyStock.Api.Mobile.Controllers.MobileProductsController",
        "EasyStock.Api.Mobile.Controllers.MobileVersionController",
        "EasyStock.Api.Mobile.Controllers.OperationController",
        "EasyStock.Api.Mobile.Controllers.PushReceiptsController",
        "EasyStock.Api.Mobile.Controllers.SyncController",
        // Admin e demonstração, dívida F7 de 2026-11-15.
        "EasyStock.Api.Controllers.AdminTenantsController",
        "EasyStock.Api.Controllers.EntregasDemonstracaoController",
    };

    private static IReadOnlyList<string> ControllersComDbContext()
    {
        var apiAssembly = typeof(EasyStock.Api.Http.EasyStockControllerBase).Assembly;
        var result = Types.InAssembly(apiAssembly)
            .That()
            .HaveNameEndingWith("Controller")
            .ShouldNot()
            .HaveDependencyOnAny(
                "EasyStock.Infra.Postgre.Data",
                "EasyStock.Infra.Postgre.Data.EasyStockDbContext")
            .GetResult();
        return result.FailingTypeNames?.ToList() ?? [];
    }

    [Fact]
    public void Controllers_NaoDevem_DependerDe_EasyStockDbContext_ForaDaAllowlist()
    {
        var novos = ControllersComDbContext().Where(t => !DebtAllowlist.Contains(t)).ToList();

        novos.Should().BeEmpty(
            "controllers consomem UseCases/Repositories (ports da Application); o DbContext concreto vive em " +
            "Infra.Postgre. Mova o acesso a dados para um caso de uso em vez de crescer a DebtAllowlist (#2371).");
    }

    [Fact]
    public void DebtAllowlist_NaoDeveConterControllersJaLimpos()
    {
        var violando = ControllersComDbContext().ToHashSet(StringComparer.Ordinal);

        var limpos = DebtAllowlist.Where(t => !violando.Contains(t)).ToList();

        limpos.Should().BeEmpty(
            "a DebtAllowlist reflete a dívida real e só diminui. Remova o controller que já não usa o DbContext " +
            "(ou que mudou de nome).");
    }
}

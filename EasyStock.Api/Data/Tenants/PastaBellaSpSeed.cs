using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Api.Data.Tenants;

/// <summary>
/// Tenant T1 — Pasta Bella SP. Persona: cliente novo, recém-cadastrado.
/// Sem lojas, sem produtos, sem dados — força o fluxo de onboarding em
/// <c>Auth/SelecionarLoja</c>. Implementação preenchida na Phase 3a.
/// </summary>
internal static class PastaBellaSpSeed
{
    public const string EmpresaNome = "Pasta Bella SP";
    public const string EmpresaDocumento = "54.318.222/0001-09";

    public static async Task ExecutarAsync(EasyStockDbContext context, DateTime agora, ILogger logger)
    {
        await Task.CompletedTask;
        logger.LogDebug("Pasta Bella SP placeholder — preenchimento na Phase 3a.");
    }
}

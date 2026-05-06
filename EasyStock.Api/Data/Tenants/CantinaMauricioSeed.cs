using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Api.Data.Tenants;

/// <summary>
/// Tenant T2 — Cantina do Maurício. Persona: pequeno comerciante, 1 loja,
/// 30 dias de operação leve. Implementação preenchida na Phase 3b.
/// </summary>
internal static class CantinaMauricioSeed
{
    public const string EmpresaNome = "Cantina do Maurício";
    public const string EmpresaDocumento = "61.402.557/0001-44";

    public static async Task ExecutarAsync(EasyStockDbContext context, DateTime agora, ILogger logger)
    {
        await Task.CompletedTask;
        logger.LogDebug("Cantina do Maurício placeholder — preenchimento na Phase 3b.");
    }
}

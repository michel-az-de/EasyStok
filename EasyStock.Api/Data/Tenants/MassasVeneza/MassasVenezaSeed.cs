using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Api.Data.Tenants.MassasVeneza;

/// <summary>
/// Tenant T4 — Massas Italianas Veneza. Persona: fabricante maduro,
/// 3 lojas, 90 dias de operação rica. Implementação dividida em arquivos
/// partial: Produtos, Clientes, Vendas, Lotes. Preenchimento na Phase 3d.
/// </summary>
internal static partial class MassasVenezaSeed
{
    public const string EmpresaNome = "Massas Italianas Veneza";
    public const string EmpresaDocumento = "72.819.116/0001-30";

    public static async Task ExecutarAsync(EasyStockDbContext context, DateTime agora, ILogger logger)
    {
        await Task.CompletedTask;
        logger.LogDebug("Massas Veneza placeholder — preenchimento na Phase 3d.");
    }
}

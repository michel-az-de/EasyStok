using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Api.Data.Tenants;

internal static partial class CasaDaBabaSeed
{
    private static async Task EnriquecerImplAsync(
        EasyStockDbContext context,
        Empresa empresa,
        Loja lojaCentro,
        Loja lojaMercadao,
        Usuario usuarioAdmin,
        Usuario usuarioGerente,
        Usuario usuarioOperadorCentro,
        Usuario usuarioOperadorMercadao,
        Dictionary<string, Produto> produtos,
        Dictionary<string, SeedData.ItemSeedContext> itens,
        Fornecedor fornFarinha,
        Fornecedor fornCarnes,
        Fornecedor fornLaticinios,
        Fornecedor fornHortifruti,
        DateTime agora,
        ILogger logger)
    {
        // Conteúdo real preenchido na Phase 3c.
        await Task.CompletedTask;
        logger.LogDebug("Casa da Baba enrichment placeholder — preenchimento na Phase 3c.");
    }
}

using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Api.Data.Tenants;

internal static partial class CasaDaBabaSeed
{
    /// <summary>
    /// Enriquecimento adicional do tenant T3: clientes B2B/B2C com endereços e telefones,
    /// 15-25 pedidos cobrindo todos status (alguns gerando Venda), pedidos a fornecedor,
    /// ~30 dias de movimento de caixa + fechamentos, lotes de produção, audit logs
    /// distribuídos em 90 dias. Implementação preenchida na Phase 3c do plano.
    /// </summary>
    private static Task EnriquecerAsync(
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
        // Implementação completa será preenchida em CasaDaBabaSeed.Enrichment.Impl.cs
        // (mantém arquivo principal estável durante refactor)
        return EnriquecerImplAsync(context, empresa, lojaCentro, lojaMercadao,
            usuarioAdmin, usuarioGerente, usuarioOperadorCentro, usuarioOperadorMercadao,
            produtos, itens, fornFarinha, fornCarnes, fornLaticinios, fornHortifruti, agora, logger);
    }
}

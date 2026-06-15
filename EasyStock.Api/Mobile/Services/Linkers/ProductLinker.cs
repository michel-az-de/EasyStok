using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Api.Mobile.Services.Linkers;

/// <summary>
/// Auto-linker: matcheia <c>Product</c> mobile com <c>Produto</c> ERP existente
/// (por nome ILIKE) ou cria novo. Loga em ProdutoAlteracao via Sistema Mobile Sync user.
/// Cria categoria default "Mobile" se a empresa não tiver nenhuma.
///
/// Extraido do god-Service <c>SyncAutoLinker</c> (F8).
/// </summary>
public sealed class ProductLinker(
    EasyStockDbContext db,
    MobileSystemUserResolver systemUserResolver,
    ILogger<ProductLinker> log)
{
    public async Task ExecuteAsync(IEnumerable<string> mobileProductIds, Guid? empresaId)
    {
        var idsList = mobileProductIds as ICollection<string> ?? mobileProductIds.ToList();
        if (!empresaId.HasValue)
        {
            // Bug silencioso historico: device nao pareado → produtos do PWA ficavam
            // orfaos em mobile_products SEM qualquer indicacao no log. Telas /produtos
            // do web vinham vazias e ninguem sabia onde olhar. Este warning torna
            // o estado explicito; rodar `POST /api/mobile/sync/backfill-erp-link`
            // de um device pareado da empresa resolve.
            log.LogWarning(
                "AutoLink Produto SKIPPED: device nao pareado (empresaId=null), {Count} produtos ficam orfaos em mobile_products",
                idsList.Count);
            return;
        }
        Guid? cachedCategoriaId = null;
        Guid? cachedSysUserId = null;
        var matched = 0;
        var created = 0;
        var idempotentSkip = 0;
        var errorSkip = 0;
        foreach (var pid in idsList)
        {
            try
            {
                var mobileP = await db.Set<Product>()
                    .FirstOrDefaultAsync(p => p.Id == pid && p.EmpresaId == empresaId);
                if (mobileP == null || mobileP.ErpProductId.HasValue) { idempotentSkip++; continue; }

                // PROD-002 (#612): o nome vem do push do PWA/app sem validacao de tags. Sanitiza
                // antes de matchear/criar o Produto ERP para nao persistir markup (XSS armazenado
                // que vazaria em PDF/etiqueta/exportacao). Em background nao da pra rejeitar:
                // sanitiza-e-loga.
                var nomeLimpo = UseCaseGuards.RemoverTagsHtml(mobileP.Name) ?? string.Empty;
                if (!string.Equals(nomeLimpo, mobileP.Name, StringComparison.Ordinal))
                    log.LogWarning("AutoLink Produto: nome do mobile continha tags HTML e foi sanitizado. mobile={MobileId} empresaId={EmpresaId}", pid, empresaId);

                var webP = await db.Set<Produto>().IgnoreQueryFilters().AsNoTracking()
                    .FirstOrDefaultAsync(p =>
                        p.EmpresaId == empresaId
                        && p.Status == StatusProduto.Ativo
                        && EF.Functions.ILike(p.Nome, nomeLimpo));

                if (webP != null)
                {
                    mobileP.ErpProductId = webP.Id;
                    matched++;
                    log.LogInformation("AutoLink Produto matched: mobile={MobileId} → erp={ErpId} via nome match", pid, webP.Id);
                    cachedSysUserId ??= await systemUserResolver.GetOrCreateAsync(empresaId.Value);
                    db.Add(new ProdutoAlteracao
                    {
                        Id = Guid.NewGuid(),
                        EmpresaId = empresaId.Value,
                        ProdutoId = webP.Id,
                        UsuarioId = cachedSysUserId.Value,
                        Acao = "atualizado",
                        AlteracoesJson = $"[{{\"campo\":\"vinculacao_mobile\",\"de\":null,\"para\":\"mobile_product={pid}\"}}]",
                        Motivo = "Sync mobile",
                        Observacao = $"Vinculado ao mobile_product {pid}",
                        AlteradoEm = DateTime.UtcNow
                    });
                    continue;
                }

                cachedCategoriaId ??= await CategoriaDefaultHelper.GetOrCreateAsync(db, log, empresaId.Value);

                var novoProd = new Produto
                {
                    Id = Guid.NewGuid(),
                    EmpresaId = empresaId.Value,
                    CategoriaId = cachedCategoriaId.Value,
                    Nome = nomeLimpo,
                    Tipo = TipoProduto.Alimento,
                    Status = StatusProduto.Ativo,
                    PrecoReferencia = mobileP.Price is { } pr && pr > 0 ? Dinheiro.FromDecimal(pr) : null,
                    CodigoBarras = mobileP.Sku,
                    ControlaValidade = mobileP.DefaultValidityDays.HasValue,
                    CriadoEm = DateTime.UtcNow,
                    AlteradoEm = DateTime.UtcNow
                };
                db.Add(novoProd);
                mobileP.ErpProductId = novoProd.Id;
                cachedSysUserId ??= await systemUserResolver.GetOrCreateAsync(empresaId.Value);
                db.Add(new ProdutoAlteracao
                {
                    Id = Guid.NewGuid(),
                    EmpresaId = empresaId.Value,
                    ProdutoId = novoProd.Id,
                    UsuarioId = cachedSysUserId.Value,
                    Acao = "cadastrado",
                    AlteracoesJson = null,
                    Motivo = "Sync mobile",
                    Observacao = $"Criado via mobile_product {pid}; Nome={novoProd.Nome}; Preco={novoProd.PrecoReferencia?.Valor}",
                    AlteradoEm = DateTime.UtcNow
                });
                created++;
                log.LogInformation("AutoLink Produto CRIADO: mobile={MobileId} → erp={ErpId} ({Nome})",
                    pid, novoProd.Id, nomeLimpo);
            }
            catch (Exception ex)
            {
                errorSkip++;
                // Antes: LogWarning sem tipo. Agora exType e Mensagem em props
                // estruturadas (Serilog/OTel) permitem filtrar por classe de erro
                // sem precisar baixar e parsear stack trace.
                log.LogError(ex,
                    "AutoLink Produto FALHOU mobile={MobileId} empresaId={EmpresaId} exType={ExType}: {Mensagem}",
                    pid, empresaId, ex.GetType().Name, ex.Message);
            }
        }
        log.LogInformation(
            "AutoLink Produto summary empresaId={EmpresaId} total={Total} matched={Matched} created={Created} idempotent={Idempotent} errors={Errors}",
            empresaId, idsList.Count, matched, created, idempotentSkip, errorSkip);
    }
}

/// <summary>
/// Helper compartilhado entre ProductLinker e BatchLinker pra resolver/criar a
/// categoria default "Mobile" da empresa. Idempotente: usa categoria existente
/// (preferindo "Mobile" → "Geral" → primeira por CriadoEm) se houver alguma.
/// </summary>
internal static class CategoriaDefaultHelper
{
    public static async Task<Guid> GetOrCreateAsync(EasyStockDbContext db, ILogger log, Guid empresaId)
    {
        var existentes = await db.Set<Categoria>().IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.EmpresaId == empresaId)
            .Select(c => new { c.Id, c.Nome, c.CriadoEm })
            .ToListAsync();
        if (existentes.Count > 0)
        {
            var preferida = existentes
                .OrderBy(c => c.Nome == "Mobile" ? 0 : (c.Nome == "Geral" ? 1 : 2))
                .ThenBy(c => c.CriadoEm)
                .First();
            return preferida.Id;
        }

        var nova = new Categoria
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Nome = "Mobile",
            Descricao = "Categoria default criada pelo auto-link mobile→ERP",
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        };
        db.Add(nova);
        log.LogInformation("AutoLink: criada Categoria 'Mobile' default empresa={EmpresaId}", empresaId);
        return nova.Id;
    }
}

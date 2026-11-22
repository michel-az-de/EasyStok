using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Domain.Entities.Storefront;
using Storefront = EasyStock.Domain.Entities.Storefront.Storefront;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;

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
    ILogger<ProductLinker> log,
    IProdutoRepository produtoRepository)
{
    // #2813: erpNames traz o nome que o produto tera no ERP, por id do app, quando difere do nome do app (primeira carga: nome +
    // embalagem). Ausente, vale o nome do app, como no sync normal.
    public async Task ExecuteAsync(IEnumerable<string> mobileProductIds, Guid? empresaId, IReadOnlyDictionary<string, string>? erpNames = null)
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
                await VincularAsync(pid);
                // #2911: grava produto a produto. O produto seguinte de mesmo nome acha este no banco, e a recusa
                // de um não leva junto clientes e pedidos do envio, que o SyncAutoLinker grava depois.
                if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                errorSkip++;
                db.DescartarMudancasPendentes();
                // A categoria e o usuário de sistema criados neste produto foram descartados junto.
                cachedCategoriaId = null;
                cachedSysUserId = null;
                log.LogError(ex,
                    "AutoLink Produto FALHOU mobile={MobileId} empresaId={EmpresaId} exType={ExType}: {Mensagem}",
                    pid, empresaId, ex.GetType().Name, ex.Message);
            }
        }
        log.LogInformation(
            "AutoLink Produto summary empresaId={EmpresaId} total={Total} matched={Matched} created={Created} idempotent={Idempotent} errors={Errors}",
            empresaId, idsList.Count, matched, created, idempotentSkip, errorSkip);

        async Task VincularAsync(string pid)
        {
            var mobileP = await db.Set<Product>()
                .FirstOrDefaultAsync(p => p.Id == pid && p.EmpresaId == empresaId);
            if (mobileP == null) { idempotentSkip++; return; }
            if (mobileP.ErpProductId.HasValue)
            {
                cachedSysUserId = await EspelharCustoEMinimoAsync(mobileP, empresaId.Value, cachedSysUserId);
                idempotentSkip++;
                return;
            }

            // PROD-002 (#612): o nome vem do push do PWA/app sem validacao de tags. Sanitiza
            // antes de matchear/criar o Produto ERP para nao persistir markup (XSS armazenado
            // que vazaria em PDF/etiqueta/exportacao). Em background nao da pra rejeitar:
            // sanitiza-e-loga.
            var nomeDoApp = erpNames is not null && erpNames.TryGetValue(pid, out var nomeNoErp) ? nomeNoErp : mobileP.Name;
            var nomeLimpo = UseCaseGuards.RemoverTagsHtml(nomeDoApp) ?? string.Empty;
            if (!string.Equals(nomeLimpo, nomeDoApp, StringComparison.Ordinal))
                log.LogWarning("AutoLink Produto: nome do mobile continha tags HTML e foi sanitizado. mobile={MobileId} empresaId={EmpresaId}", pid, empresaId);

            // #2911: o inativo de mesmo nome também casa (sem reativar). Antes o linker tentava criar outro
            // Produto com o nome dele, o índice único (EmpresaId, Nome) recusava e o envio perdia o vínculo.
            var webP = await db.Set<Produto>().IgnoreQueryFilters().AsNoTracking()
                .Where(p =>
                    p.EmpresaId == empresaId
                    // #2477: "%" e "_" no nome do app são texto; sem escape, "Bolo_" casava com "Bolos".
                    && EF.Functions.ILike(p.Nome, PadraoILike.Literal(nomeLimpo), PadraoILike.Escape))
                .OrderBy(p => p.Status == StatusProduto.Ativo ? 0 : 1)
                .FirstOrDefaultAsync();

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
                return;
            }

            var produtoDoPrato = await ProdutoDoPratoAsync(nomeLimpo, empresaId.Value, pid);
            if (produtoDoPrato.HasValue)
            {
                mobileP.ErpProductId = produtoDoPrato.Value;
                matched++;
                log.LogInformation("AutoLink Produto matched: mobile={MobileId} → erp={ErpId} via prato do cardapio", pid, produtoDoPrato.Value);
                cachedSysUserId ??= await systemUserResolver.GetOrCreateAsync(empresaId.Value);
                db.Add(new ProdutoAlteracao
                {
                    Id = Guid.NewGuid(),
                    EmpresaId = empresaId.Value,
                    ProdutoId = produtoDoPrato.Value,
                    UsuarioId = cachedSysUserId.Value,
                    Acao = "atualizado",
                    AlteracoesJson = $"[{{\"campo\":\"vinculacao_mobile\",\"de\":null,\"para\":\"mobile_product={pid}\"}}]",
                    Motivo = "Sync mobile",
                    Observacao = $"Vinculado ao mobile_product {pid} pelo nome do prato do cardápio",
                    AlteradoEm = DateTime.UtcNow
                });
                return;
            }

            // #2813 B8: o console lê a produção pelo produto do prato. Prato avulso (sem produto) com o nome do
            // produto do app nasce ligado ao Produto novo, em vez de o Produto ficar órfão.
            var pratoAvulso = await PratoAvulsoPorNomeAsync(nomeLimpo, empresaId.Value, pid);

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
                CustoReferencia = mobileP.Cost is { } cu && cu > 0 ? Dinheiro.FromDecimal(cu) : null,
                QuantidadeMinima = mobileP.MinStock,
                CodigoBarras = mobileP.Sku,
                ControlaValidade = mobileP.DefaultValidityDays.HasValue,
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow
            };
            await produtoRepository.InsertAsync(novoProd);
            if (pratoAvulso != null)
            {
                pratoAvulso.VincularProduto(novoProd.Id);
                log.LogInformation("AutoLink Produto: prato avulso {PratoId} ligado ao produto novo erp={ErpId}", pratoAvulso.Id, novoProd.Id);
            }
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
    }

    /// <summary>
    /// #2083 A2: o console lê a produção pelo produto do prato (<c>CardapioItem.ProdutoId</c>). Sem Produto de mesmo
    /// nome, o produto do app com o nome de um prato ativo liga ao produto desse prato, em vez de virar um Produto
    /// órfão. Nome comparado sem espaços nas pontas, sem diferenciar maiúsculas e com "%"/"_" literais (#2477).
    /// Dois pratos de mesmo nome em produtos diferentes: não adivinha, devolve null e o fluxo cria como antes.
    /// O cardápio é um por empresa (sem escopo de loja) e o lote do app não diz porção: liga ao produto, nunca a uma
    /// variação.
    /// </summary>
    private async Task<Guid?> ProdutoDoPratoAsync(string nome, Guid empresaId, string mobileId)
    {
        var nomeAparado = nome.Trim();
        if (nomeAparado.Length == 0) return null;
        var padrao = PadraoILike.Literal(nomeAparado);

        var produtos = await (
                from prato in db.Set<CardapioItem>().IgnoreQueryFilters()
                join loja in db.Set<Storefront>().IgnoreQueryFilters() on prato.StorefrontId equals loja.Id
                join produto in db.Set<Produto>().IgnoreQueryFilters() on prato.ProdutoId equals produto.Id
                where loja.EmpresaId == empresaId
                      && produto.EmpresaId == empresaId
                      && produto.Status == StatusProduto.Ativo
                      && prato.ArquivadoEm == null
                      && prato.NomePublico != null
                      && EF.Functions.ILike(prato.NomePublico.Trim(), padrao, PadraoILike.Escape)
                select produto.Id)
            .Distinct()
            .Take(2)
            .ToListAsync();

        if (produtos.Count == 1) return produtos[0];
        if (produtos.Count > 1)
            log.LogWarning(
                "AutoLink Produto: nome do mobile casa pratos de produtos diferentes; nao vincula a nenhum. mobile={MobileId} empresaId={EmpresaId}",
                mobileId, empresaId);
        return null;
    }

    /// <summary>
    /// #2813 B8: o console lê a produção pelo produto do prato (<c>CardapioItem.ProdutoId</c>). Prato avulso (sem produto)
    /// com o nome do produto do app, que não casou com Produto nem com prato já ligado, é ligado ao Produto novo, em vez de
    /// o Produto nascer órfão. Mesma regra de nome de <see cref="ProdutoDoPratoAsync"/>. Só liga quando é o ÚNICO prato não
    /// arquivado da empresa com esse nome e ele não tem produto: dois ou mais pratos de mesmo nome não liga, e prato que
    /// já tem produto nunca é trocado.
    /// </summary>
    private async Task<CardapioItem?> PratoAvulsoPorNomeAsync(string nome, Guid empresaId, string mobileId)
    {
        var nomeAparado = nome.Trim();
        if (nomeAparado.Length == 0) return null;
        var padrao = PadraoILike.Literal(nomeAparado);

        var pratos = await (
                from prato in db.Set<CardapioItem>().IgnoreQueryFilters()
                join loja in db.Set<Storefront>().IgnoreQueryFilters() on prato.StorefrontId equals loja.Id
                where loja.EmpresaId == empresaId
                      && prato.ArquivadoEm == null
                      && prato.NomePublico != null
                      && EF.Functions.ILike(prato.NomePublico.Trim(), padrao, PadraoILike.Escape)
                select prato)
            .Take(2)
            .ToListAsync();

        if (pratos.Count > 1)
            log.LogWarning(
                "AutoLink Produto: nome do mobile casa mais de um prato; nao liga o prato avulso. mobile={MobileId} empresaId={EmpresaId}",
                mobileId, empresaId);

        // Prato ligado neste mesmo lote (ainda sem SaveChanges) volta rastreado com o ProdutoId novo: não é mais avulso.
        return pratos.Count == 1 && pratos[0].ProdutoId is null ? pratos[0] : null;
    }

    /// <summary>
    /// #1467: produto ja vinculado. Custo e estoque minimo editados no PWA vao para o Produto ERP.
    /// So valores informados viajam: PWA sem custo nao apaga o custo do ERP.
    /// </summary>
    private async Task<Guid?> EspelharCustoEMinimoAsync(Product mobileP, Guid empresaId, Guid? cachedSysUserId)
    {
        if (mobileP.Cost is not > 0 && mobileP.MinStock is null) return cachedSysUserId;

        var produto = await db.Set<Produto>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == mobileP.ErpProductId && p.EmpresaId == empresaId);
        if (produto == null) return cachedSysUserId;

        var mudancas = new List<string>();
        if (mobileP.Cost is { } custo && custo > 0 && produto.CustoReferencia?.Valor != custo)
        {
            mudancas.Add($"{{\"campo\":\"custo\",\"de\":{Json(produto.CustoReferencia?.Valor)},\"para\":{Json(custo)}}}");
            // #2401: o custo de 6 casas do insumo acompanha, senão a ficha segue no valor velho.
            if (produto.EhInsumo) produto.DefinirCustoDoInsumo(custo);
            else
            {
                produto.CustoReferencia = Dinheiro.FromDecimal(custo);
                produto.CustoUnitarioInsumo = null;
            }
        }
        if (mobileP.MinStock is { } minimo && produto.QuantidadeMinima != minimo)
        {
            mudancas.Add($"{{\"campo\":\"quantidade_minima\",\"de\":{Json(produto.QuantidadeMinima)},\"para\":{minimo}}}");
            produto.QuantidadeMinima = minimo;
        }
        if (mudancas.Count == 0) return cachedSysUserId;

        produto.AlteradoEm = DateTime.UtcNow;
        cachedSysUserId ??= await systemUserResolver.GetOrCreateAsync(empresaId);
        db.Add(new ProdutoAlteracao
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = produto.Id,
            UsuarioId = cachedSysUserId.Value,
            Acao = "atualizado",
            AlteracoesJson = "[" + string.Join(",", mudancas) + "]",
            Motivo = "Sync mobile",
            Observacao = $"Custo/minimo editados no mobile_product {mobileP.Id}",
            AlteradoEm = DateTime.UtcNow
        });
        log.LogInformation("Produto ERP {ErpId} recebeu custo/minimo do mobile {MobileId}", produto.Id, mobileP.Id);
        return cachedSysUserId;
    }

    private static string Json(decimal? v) =>
        v.HasValue ? v.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "null";

    private static string Json(int? v) => v.HasValue ? v.Value.ToString() : "null";
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

using EasyStock.Domain.Entities.Mobile;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Api.Mobile.Services.Linkers;

/// <summary>
/// Auto-linker: promove <c>Batch</c> mobile a <c>Lote</c> ERP. Cria ItemEstoque
/// + MovimentacaoEstoque (Entrada) idempotente para cada LoteItem (F8-A).
/// Idempotente via FindByMobileBatchIdAsync + CodigoInterno="lote:loteId:produtoId".
///
/// Extraido do god-Service <c>SyncAutoLinker</c> (F8).
/// </summary>
public sealed class BatchLinker(
    EasyStockDbContext db,
    ILoteRepository loteRepo,
    ILogger<BatchLinker> log)
{
    public async Task ExecuteAsync(IEnumerable<string> mobileBatchIds, Guid? empresaId)
    {
        var idsList = mobileBatchIds as ICollection<string> ?? mobileBatchIds.ToList();
        if (!empresaId.HasValue)
        {
            log.LogWarning(
                "AutoLink Lote SKIPPED: device nao pareado (empresaId=null), {Count} lotes ficam orfaos em mobile_batches",
                idsList.Count);
            return;
        }
        var created = 0;
        var idempotentSkip = 0;
        var errorSkip = 0;
        foreach (var bid in idsList)
        {
            try
            {
                var mobileB = await db.Set<Batch>().IgnoreQueryFilters()
                    .Include(b => b.Items)
                    .FirstOrDefaultAsync(b => b.Id == bid && b.EmpresaId == empresaId);
                if (mobileB == null) { idempotentSkip++; continue; }
                if (mobileB.ErpLoteId.HasValue && mobileB.ErpLoteId.Value != Guid.Empty) { idempotentSkip++; continue; }

                var jaPromovido = await loteRepo.FindByMobileBatchIdAsync(empresaId.Value, mobileB.Id);
                if (jaPromovido != null)
                {
                    mobileB.ErpLoteId = jaPromovido.Id;
                    idempotentSkip++;
                    log.LogInformation("AutoLink Lote (idempotente): mobile={MobileId} → erp={ErpId}", bid, jaPromovido.Id);
                    continue;
                }

                var codigoBase = !string.IsNullOrWhiteSpace(mobileB.Lote)
                    ? mobileB.Lote!
                    : !string.IsNullOrWhiteSpace(mobileB.Code)
                        ? mobileB.Code!
                        : $"LOT-{mobileB.CreatedAt:yyMMdd}";
                var sufixo = mobileB.Id.Length >= 6
                    ? mobileB.Id.Substring(mobileB.Id.Length - 6)
                    : mobileB.Id;
                sufixo = new string(sufixo.Where(c => char.IsLetterOrDigit(c)).ToArray());
                var codigo = string.IsNullOrEmpty(sufixo) ? codigoBase : (codigoBase + "-" + sufixo);

                var lote = Lote.Criar(empresaId.Value, codigo, mobileB.CreatedAt, mobileB.LojaId);
                lote.MobileBatchId = mobileB.Id;
                lote.OperadorNome = mobileB.LastOperatorName;
                lote.Origem = "mobile";

                foreach (var item in mobileB.Items)
                {
                    Guid? produtoIdResolved = null;
                    if (!string.IsNullOrWhiteSpace(item.ProductId))
                    {
                        var mProd = await db.Set<Product>().IgnoreQueryFilters().AsNoTracking()
                            .FirstOrDefaultAsync(p => p.Id == item.ProductId);
                        produtoIdResolved = mProd?.ErpProductId;
                    }
                    lote.Itens.Add(new LoteItem
                    {
                        Id = Guid.NewGuid(),
                        LoteId = lote.Id,
                        ProdutoId = produtoIdResolved,
                        Nome = item.Name,
                        Emoji = item.Emoji,
                        Unidade = item.Unit,
                        Quantidade = item.Qty,
                        PesoG = item.WeightG,
                        ValidadeDias = item.ValidityDays,
                        ExpiraEm = item.ExpiresAt,
                        CriadoEm = DateTime.UtcNow
                    });
                }

                lote.Finalizar();
                await loteRepo.AddAsync(lote);
                mobileB.ErpLoteId = lote.Id;
                if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync();
                created++;
                log.LogInformation("F2 Lote CRIADO: mobile={MobileId} → erp={ErpId} itens={N}",
                    bid, lote.Id, lote.Itens.Count);

                await EnsureEntradaEstoqueDoLoteAsync(lote);
            }
            catch (Exception ex)
            {
                errorSkip++;
                log.LogError(ex,
                    "F2 AutoLink Lote FALHOU mobile={MobileId} empresaId={EmpresaId} exType={ExType}: {Mensagem}",
                    bid, empresaId, ex.GetType().Name, ex.Message);
            }
        }
        log.LogInformation(
            "AutoLink Lote summary empresaId={EmpresaId} total={Total} created={Created} idempotent={Idempotent} errors={Errors}",
            empresaId, idsList.Count, created, idempotentSkip, errorSkip);
    }

    /// <summary>
    /// F8-A — Cria ItemEstoque + MovimentacaoEstoque (Entrada) para cada LoteItem.
    /// Idempotente: CodigoInterno="lote:loteId:produtoId".
    /// </summary>
    private async Task EnsureEntradaEstoqueDoLoteAsync(Lote lote)
    {
        foreach (var item in lote.Itens.Where(i => i.ProdutoId.HasValue && i.Quantidade > 0))
        {
            try
            {
                var codigoInterno = $"lote:{lote.Id}:{item.ProdutoId}";
                var jaTem = await db.Set<ItemEstoque>().IgnoreQueryFilters().AsNoTracking()
                    .AnyAsync(ie => ie.EmpresaId == lote.EmpresaId && ie.CodigoInterno == codigoInterno);
                if (jaTem)
                {
                    log.LogInformation("F8-A skip (idempotente): lote={LoteId} produto={ProdutoId} codigoInterno={Codigo}",
                        lote.Id, item.ProdutoId, codigoInterno);
                    continue;
                }

                var produto = await db.Set<Produto>().IgnoreQueryFilters().AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == item.ProdutoId);
                if (produto == null)
                {
                    log.LogWarning("F8-A: produto {ProdutoId} nao existe; pulando entrada do lote {LoteId}",
                        item.ProdutoId, lote.Id);
                    continue;
                }

                var custoUnit = produto.CustoReferencia ?? Dinheiro.Zero;
                var precoVenda = produto.PrecoReferencia;
                Validade? validade = item.ExpiraEm.HasValue ? Validade.From(item.ExpiraEm.Value) : null;
                var qtd = Quantidade.From(item.Quantidade);

                var itemEstoque = ItemEstoque.CriarParaEntrada(
                    id: Guid.NewGuid(),
                    empresaId: lote.EmpresaId,
                    produto: produto,
                    variacao: null,
                    quantidade: qtd,
                    custoUnitario: custoUnit,
                    precoVendaSugerido: precoVenda,
                    dataEntrada: lote.DataProducao,
                    codigoInterno: codigoInterno,
                    codigoLote: null,
                    codigoMarketplace: null,
                    variacaoDescricao: null,
                    cor: null,
                    tamanho: null,
                    descricaoAnuncio: null,
                    dimensoesReais: null,
                    fornecedorNome: null,
                    validade: validade,
                    observacoes: $"Auto-gerado pelo F8-A a partir do Lote {lote.Codigo}",
                    criadoEm: DateTime.UtcNow);
                if (lote.LojaId.HasValue) itemEstoque.LojaId = lote.LojaId;

                db.Add(itemEstoque);

                var movimentacao = MovimentacaoEstoque.CriarEntrada(
                    id: Guid.NewGuid(),
                    empresaId: lote.EmpresaId,
                    item: itemEstoque,
                    natureza: NaturezaMovimentacaoEstoque.Producao,
                    quantidade: qtd,
                    valorUnitario: custoUnit,
                    dataMovimentacao: lote.DataProducao,
                    descricao: $"Producao lote {lote.Codigo}",
                    documentoReferencia: $"lote:{lote.Id}",
                    criadoEm: DateTime.UtcNow);
                db.Add(movimentacao);

                await db.SaveChangesAsync();
                log.LogInformation(
                    "F8-A ItemEstoque + Movimentacao CRIADOS: lote={LoteId} produto={ProdutoId} qtd={Qtd}",
                    lote.Id, item.ProdutoId, item.Quantidade);
            }
            catch (Exception ex)
            {
                log.LogError(ex,
                    "F8-A FALHOU pra lote={LoteId} produto={ProdutoId}: {Tipo}: {Mensagem}",
                    lote.Id, item.ProdutoId, ex.GetType().Name, ex.Message);
            }
        }
    }
}

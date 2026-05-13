using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.Mobile.Services;

/// <summary>
/// Onda 3 — sincronização de vendas mobile com o ERP.
///
/// Quando um <see cref="Order"/> mobile transiciona pra status
/// <c>"entregue"</c>, este service cria uma <c>Venda</c> + <c>ItemVenda</c>s
/// no ERP. Vendas aparecem no Analytics, dashboards, e podem virar NF
/// no fluxo padrão do ERP.
///
/// Estratégia:
///   - Idempotência via <see cref="Order.ErpVendaId"/>: se já tem Venda,
///     não cria de novo (re-envio de mutation é seguro).
///   - Só cria itens pra produtos linkados ao ERP (<see cref="Product.ErpProductId"/>);
///     itens não-linkados são ignorados (entram só na Venda como observação).
///   - ValorTotal vem da somatória dos PrecoTotal — agregação automática.
///   - Canal=<see cref="CanalVenda.Outro"/> + Observacoes="Mobile" pra
///     filtros futuros.
///
/// FAIL-SAFE: qualquer exceção é loggada mas NÃO propaga. Sync do app
/// continua offline-first; Order no banco fica sem ErpVendaId e gestor
/// pode forçar reconciliação depois.
/// </summary>
public class MobileSaleSyncService(
    EasyStockDbContext db,
    ILogger<MobileSaleSyncService> log)
{
    private readonly EasyStockDbContext _db = db;
    private readonly ILogger<MobileSaleSyncService> _log = log;

    /// <summary>
    /// Cria Venda + ItemVenda no ERP a partir de um Order mobile entregue.
    /// Idempotente: se já tem ErpVendaId, retorna sem fazer nada.
    /// </summary>
    public async Task<bool> CreateVendaForDeliveredOrderAsync(
        Order order,
        IList<DTOs.OrderItemDto> items,
        CancellationToken ct = default)
    {
        try
        {
            if (order.ErpVendaId.HasValue) return false; // idempotente
            if (order.EmpresaId == null) return false;
            if (items == null || items.Count == 0) return false;

            // Resolve mobile_products pra cada item — só os linkados viram ItemVenda.
            // IgnoreQueryFilters: backfill/endpoint mobile não tem JWT, então o
            // Global Query Filter tenant zeraria (CurrentTenantId=Guid.Empty).
            // Filtragem manual por EmpresaId já garante o isolamento.
            var mobileIds = items.Select(i => i.ProductId).Distinct().ToList();
            var products = await _db.Set<Product>().IgnoreQueryFilters().AsNoTracking()
                .Where(p => p.EmpresaId == order.EmpresaId && mobileIds.Contains(p.Id))
                .ToListAsync(ct);

            var linkedItems = items
                .Select(i => new
                {
                    Item = i,
                    Product = products.FirstOrDefault(p => p.Id == i.ProductId)
                })
                .Where(x => x.Product?.ErpProductId != null)
                .ToList();

            if (linkedItems.Count == 0)
            {
                _log.LogInformation(
                    "Onda 3: pedido {OrderId} entregue mas nenhum produto está linkado ao ERP. Venda não criada (vai aparecer só nos analytics quando produtos forem aprovados).",
                    order.Id);
                return false;
            }

            // Cria a Venda
            var vendaId = Guid.NewGuid();
            var venda = Venda.Criar(
                id: vendaId,
                empresaId: order.EmpresaId.Value,
                canal: CanalVenda.Outro,
                natureza: NaturezaMovimentacaoEstoque.Venda,
                dataVenda: order.UpdatedAt,
                dataEnvio: null,
                numeroNotaFiscal: null,
                observacoes: $"Mobile · pedido #{order.Id.Substring(Math.Max(0, order.Id.Length - 8))} · {order.ClientSnapshotName}",
                criadoEm: DateTime.UtcNow);
            venda.LojaId = order.LojaId;
            _db.Add(venda);

            // Pra criar ItemVenda precisa de ItemEstoqueId. Localiza por Produto+Loja.
            // Se não tem ItemEstoque, item vai pro log e segue (sem item, sem ERP stock match).
            foreach (var li in linkedItems)
            {
                var produtoId = li.Product!.ErpProductId!.Value;
                // IgnoreQueryFilters: vide comentario no SELECT de products acima.
                var itemEstoque = await _db.Set<ItemEstoque>().IgnoreQueryFilters().AsNoTracking()
                    .Where(ie => ie.EmpresaId == order.EmpresaId &&
                                 ie.ProdutoId == produtoId &&
                                 (ie.LojaId == null || ie.LojaId == order.LojaId))
                    .FirstOrDefaultAsync(ct);

                if (itemEstoque == null)
                {
                    // F8-I: auto-cria ItemEstoque com qtd=0 pra produtos vendidos
                    // sem entrada previa (ad-hoc). Sem isso a Venda nasce vazia
                    // e perde-se rastreabilidade. Saldo negativo é a verdade —
                    // produto vendido sem entrada registrada.
                    var produto = await _db.Set<Produto>().IgnoreQueryFilters().AsNoTracking()
                        .FirstOrDefaultAsync(p => p.Id == produtoId, ct);
                    if (produto == null)
                    {
                        _log.LogWarning(
                            "Onda 3: produto {ErpId} nao existe — ItemVenda não criado. Pedido {OrderId}",
                            produtoId, order.Id);
                        continue;
                    }

                    var custoUnit = produto.CustoReferencia ?? Dinheiro.Zero;
                    var novoItem = ItemEstoque.CriarParaEntrada(
                        id: Guid.NewGuid(),
                        empresaId: order.EmpresaId.Value,
                        produto: produto,
                        variacao: null,
                        quantidade: Quantidade.Zero,
                        custoUnitario: custoUnit,
                        precoVendaSugerido: produto.PrecoReferencia,
                        dataEntrada: DateTime.UtcNow,
                        codigoInterno: $"AUTO-{Guid.NewGuid().ToString("N").Substring(0, 8)}",
                        codigoLote: null,
                        codigoMarketplace: null,
                        variacaoDescricao: null,
                        cor: null,
                        tamanho: null,
                        descricaoAnuncio: null,
                        dimensoesReais: null,
                        fornecedorNome: null,
                        validade: null,
                        observacoes: $"Auto-criado pela Onda 3 — produto vendido (pedido mobile {order.Id}) sem entrada previa de estoque",
                        criadoEm: DateTime.UtcNow);
                    if (order.LojaId.HasValue) novoItem.LojaId = order.LojaId;
                    _db.Add(novoItem);
                    itemEstoque = novoItem;
                    _log.LogInformation(
                        "Onda 3: ItemEstoque auto-criado (qtd=0) pra produto {ErpId} (mobile {MobileId}) loja {LojaId} pedido {OrderId}",
                        produtoId, li.Product.Id, order.LojaId, order.Id);
                }

                var itemVenda = new ItemVenda
                {
                    Id = Guid.NewGuid(),
                    VendaId = vendaId,
                    ItemEstoqueId = itemEstoque.Id,
                    ProdutoId = produtoId,
                    ProdutoVariacaoId = itemEstoque.ProdutoVariacaoId,
                    DescricaoSnapshot = li.Item.Name,
                    Quantidade = Quantidade.From(li.Item.Qty),
                    PrecoUnitario = Dinheiro.FromDecimal(li.Item.UnitPrice),
                    PrecoTotal = Dinheiro.FromDecimal(li.Item.UnitPrice * li.Item.Qty),
                    CriadoEm = DateTime.UtcNow
                };
                venda.AdicionarItem(itemVenda);
                _db.Add(itemVenda);
            }

            order.ErpVendaId = vendaId;

            _log.LogInformation(
                "Onda 3: Venda {VendaId} criada pra pedido mobile {OrderId} ({ItemsCount} itens, total {Total})",
                vendaId, order.Id, venda.ItensVenda?.Count ?? 0, venda.ValorTotal.Valor);
            return true;
        }
        catch (Exception ex)
        {
            _log.LogError(ex,
                "Onda 3: falha ao criar Venda pro pedido {OrderId}. Sync continua, ErpVendaId fica null.",
                order.Id);
            return false;
        }
    }

    /// <summary>
    /// Cancelamento de pedido entregue: registra observação na Venda
    /// existente. Não deleta — preserva audit trail. Movimentação de
    /// estoque já é tratada pelo MobileStockReconciler (Onda 2.2).
    /// </summary>
    public async Task<bool> CancelVendaForOrderAsync(Order order, CancellationToken ct = default)
    {
        try
        {
            if (!order.ErpVendaId.HasValue) return false;

            // IgnoreQueryFilters: endpoint mobile sem JWT (CurrentTenantId=Empty).
            var venda = await _db.Set<Venda>().IgnoreQueryFilters()
                .FirstOrDefaultAsync(v => v.Id == order.ErpVendaId, ct);
            if (venda == null) return false;

            var prefix = "[CANCELADO no mobile em " + DateTime.UtcNow.ToString("dd/MM HH:mm") + "] ";
            venda.Observacoes = prefix + (venda.Observacoes ?? "");

            _log.LogInformation(
                "Onda 3: Venda {VendaId} marcada como cancelada (pedido mobile {OrderId})",
                venda.Id, order.Id);
            return true;
        }
        catch (Exception ex)
        {
            _log.LogError(ex,
                "Onda 3: falha ao cancelar Venda do pedido {OrderId}. Sync continua.",
                order.Id);
            return false;
        }
    }
}

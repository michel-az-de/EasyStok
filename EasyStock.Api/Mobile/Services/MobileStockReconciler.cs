using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.Mobile.Services;

/// <summary>
/// Onda 2 parte 2 — reconciliação de estoque mobile <-> ERP.
///
/// Quando um <see cref="Product"/> mobile está linkado a um <c>Produto</c>
/// ERP (ErpProductId != null) E pertence a uma loja (LojaId != null),
/// mutações de estoque vindas do app refletem-se também em
/// <c>itens_estoque</c> + <c>movimentacoes_estoque</c> do ERP.
///
/// Estratégia:
///   - Localiza ItemEstoque do (ProdutoId, LojaId, EmpresaId).
///   - Se existe: aplica delta na QuantidadeAtual via Add/Subtract.
///   - Cria MovimentacaoEstoque com natureza apropriada (Venda, Producao,
///     Estorno, etc).
///   - Se NÃO existe ItemEstoque: log warning. O operador web cria via
///     /produtos-mobile (futura aba "Divergências") ou via fluxo normal
///     do ERP. Não bloqueia o sync — app continua offline-first.
///
/// FAIL-SAFE: qualquer exceção neste fluxo é loggada mas NÃO propaga.
/// Sync do app não pode quebrar por falha do ERP — fila persiste local
/// e reconciler tenta de novo no próximo flush.
/// </summary>
public class MobileStockReconciler(
    EasyStockDbContext db,
    ILogger<MobileStockReconciler> log)
{
    private readonly EasyStockDbContext _db = db;
    private readonly ILogger<MobileStockReconciler> _log = log;

    /// <summary>
    /// Aplica delta de estoque no ERP, refletindo uma operação do app.
    /// </summary>
    /// <param name="mobileProduct">Produto mobile (lê ErpProductId, EmpresaId, LojaId).</param>
    /// <param name="deltaQty">Delta SIGNED. Negativo = saída (venda); positivo = entrada (produção/estorno).</param>
    /// <param name="natureza">Natureza da movimentação no ERP.</param>
    /// <param name="descricao">Texto livre pra audit (ex: "Pedido #abc123 entregue").</param>
    /// <param name="referenciaDocumento">Id do documento origem (ex: order.Id, batch.Id).</param>
    /// <returns>true se reconciliou, false se pulou (não linkado, ou ItemEstoque ausente).</returns>
    public async Task<bool> ApplyDeltaAsync(
        Product mobileProduct,
        int deltaQty,
        NaturezaMovimentacaoEstoque natureza,
        string descricao,
        string? referenciaDocumento,
        CancellationToken ct = default)
    {
        try
        {
            // Sem link ERP ou sem loja: sync local apenas, sem reconciliação.
            if (mobileProduct.ErpProductId == null) return false;
            if (mobileProduct.EmpresaId == null) return false;
            if (deltaQty == 0) return false;

            var produtoId = mobileProduct.ErpProductId.Value;
            var empresaId = mobileProduct.EmpresaId.Value;
            var lojaId = mobileProduct.LojaId; // nullable — ItemEstoque pode ser sem loja

            // Procura ItemEstoque existente. Prefere match exato (loja+produto),
            // cai pra primeiro com produto se o ItemEstoque foi cadastrado sem loja.
            var item = await _db.Set<ItemEstoque>()
                .FirstOrDefaultAsync(i =>
                    i.EmpresaId == empresaId &&
                    i.ProdutoId == produtoId &&
                    (lojaId == null || i.LojaId == lojaId), ct);

            if (item == null)
            {
                _log.LogWarning(
                    "Reconciliação mobile->ERP: ItemEstoque ausente pra produto {ErpProductId} loja {LojaId} (mobile_product.Id={MobileId}). Crie manualmente no ERP pra ativar reconciliação.",
                    produtoId, lojaId, mobileProduct.Id);
                return false;
            }

            // Aplica delta na QuantidadeAtual. Subtract lança se for negativo —
            // aqui clampamos pra Zero pra não bloquear sync (mobile pode estar
            // dessincronizado). ERP fica consistente, mobile reflete na próxima pull.
            var atual = item.QuantidadeAtual?.Value ?? 0;
            var novo = atual + deltaQty;
            if (novo < 0)
            {
                _log.LogWarning(
                    "Reconciliação mobile->ERP: delta {Delta} resultaria em estoque negativo ({Atual} + {Delta}) pra item {ItemId}. Clampando pra Zero.",
                    deltaQty, atual, deltaQty, item.Id);
                novo = 0;
            }
            item.QuantidadeAtual = Quantidade.From(novo);
            item.UltimaMovimentacaoEm = DateTime.UtcNow;
            item.AlteradoEm = DateTime.UtcNow;

            // Cria MovimentacaoEstoque correspondente.
            var tipo = deltaQty < 0 ? TipoMovimentacaoEstoque.Saida : TipoMovimentacaoEstoque.Entrada;
            var qty = Quantidade.From(Math.Abs(deltaQty));
            var movimentacao = new MovimentacaoEstoque
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaId,
                ItemEstoqueId = item.Id,
                ProdutoId = produtoId,
                ProdutoVariacaoId = item.ProdutoVariacaoId,
                Tipo = tipo,
                Natureza = natureza,
                Quantidade = qty,
                ValorUnitario = item.CustoUnitario,
                ValorTotal = item.CustoUnitario != null
                    ? Dinheiro.FromDecimal(item.CustoUnitario.Valor * Math.Abs(deltaQty))
                    : null,
                DataMovimentacao = DateTime.UtcNow,
                Descricao = descricao,
                DocumentoReferencia = referenciaDocumento,
                CriadoEm = DateTime.UtcNow
            };
            _db.Add(movimentacao);

            // Espelha a quantidade no mobile_product (fonte da verdade local pro app)
            mobileProduct.Stock = novo;
            mobileProduct.UpdatedAt = DateTime.UtcNow;

            _log.LogInformation(
                "Reconciliação mobile->ERP OK: produto={ErpId} loja={LojaId} delta={Delta} novoEstoque={Novo} natureza={Nat} doc={Doc}",
                produtoId, lojaId, deltaQty, novo, natureza, referenciaDocumento);
            return true;
        }
        catch (Exception ex)
        {
            // FAIL-SAFE absoluto: log e segue. Sync do app não pode quebrar
            // porque ERP teve hiccup — fila persiste local e tenta de novo.
            _log.LogError(ex,
                "Falha em MobileStockReconciler.ApplyDeltaAsync para produto={MobileId}. Sync segue, reconciliação pendente.",
                mobileProduct.Id);
            return false;
        }
    }
}

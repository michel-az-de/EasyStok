using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.Mobile.Services;

/// <summary>
/// Onda 2 parte 2 — reconciliação de estoque mobile (Product) com ERP (Produto).
/// <para>
/// Quando um <see cref="Product"/> mobile está linkado a um Produto
/// ERP (ErpProductId != null) E pertence a uma loja (LojaId != null),
/// mutações de estoque vindas do app refletem-se também em
/// itens_estoque e movimentacoes_estoque do ERP.
/// </para>
/// <para>
/// Estratégia: localiza ItemEstoque por (ProdutoId, LojaId, EmpresaId);
/// se existe, aplica delta na QuantidadeAtual via Add/Subtract e cria
/// MovimentacaoEstoque com natureza apropriada (Venda, Producao, Estorno).
/// Se nao existe, loga warning — operador web cria via /produtos-mobile
/// (aba "Divergencias") ou via fluxo normal do ERP. Nao bloqueia o sync,
/// app continua offline-first.
/// </para>
/// <para>
/// FAIL-SAFE: qualquer excecao neste fluxo e loggada mas NAO propaga.
/// Sync do app nao pode quebrar por falha do ERP — fila persiste local
/// e reconciler tenta de novo no proximo flush.
/// </para>
/// </summary>
public class MobileStockReconciler(
    EasyStockDbContext db,
    MobileSystemUserResolver systemUserResolver,
    ILogger<MobileStockReconciler> log)
{
    private readonly EasyStockDbContext _db = db;
    private readonly MobileSystemUserResolver _systemUserResolver = systemUserResolver;
    private readonly ILogger<MobileStockReconciler> _log = log;

    /// <summary>
    /// Aplica delta de estoque no ERP, refletindo uma operação do app.
    /// </summary>
    /// <param name="mobileProduct">Produto mobile (lê ErpProductId, EmpresaId, LojaId).</param>
    /// <param name="deltaQty">Delta SIGNED. Negativo = saída (venda); positivo = entrada (produção/estorno).</param>
    /// <param name="natureza">Natureza da movimentação no ERP.</param>
    /// <param name="descricao">Texto livre pra audit (ex: "Pedido #abc123 entregue").</param>
    /// <param name="referenciaDocumento">Id do documento origem (ex: order.Id, batch.Id).</param>
    /// <param name="ct">Token de cancelamento.</param>
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

            // Procura ItemEstoque existente. Match exato por loja primeiro;
            // só cai pro fallback (loja=null) se o app está em modo sem loja
            // amarrada. Isso evita que device A consuma estoque da loja B
            // silenciosamente em multi-loja.
            // IgnoreQueryFilters: chamado de endpoints mobile sem JWT (CurrentTenantId=Empty),
            // o Global Query Filter zeraria o resultado. Tenant isolation ja garantido
            // pelo filtro manual i.EmpresaId == empresaId (vem de mobileProduct.EmpresaId).
            var item = lojaId.HasValue
                ? await _db.Set<ItemEstoque>().IgnoreQueryFilters().FirstOrDefaultAsync(i =>
                    i.EmpresaId == empresaId &&
                    i.ProdutoId == produtoId &&
                    i.LojaId == lojaId, ct)
                : await _db.Set<ItemEstoque>().IgnoreQueryFilters().FirstOrDefaultAsync(i =>
                    i.EmpresaId == empresaId &&
                    i.ProdutoId == produtoId &&
                    i.LojaId == null, ct);

            if (item == null)
            {
                _log.LogWarning(
                    "Reconciliação mobile->ERP: ItemEstoque ausente pra produto {ErpProductId} loja {LojaId} (mobile_product.Id={MobileId}). Crie manualmente no ERP pra ativar reconciliação.",
                    produtoId, lojaId, mobileProduct.Id);
                return false;
            }

            // Aplica delta na QuantidadeAtual. Se resulta em negativo,
            // ainda aplica o delta possível (até zero) e ENFILEIRA divergência
            // para reconciliação manual. Antes era clamp silencioso → divergência permanente
            // sem rastro. Agora gera entrada em ProdutoAlteracao com tipo "DivergenciaSync"
            // pra operador investigar.
            var atual = item.QuantidadeAtual?.Value ?? 0;
            var novo = atual + deltaQty;
            var divergiu = false;
            if (novo < 0)
            {
                divergiu = true;
                _log.LogError(
                    "Divergência mobile↔ERP: delta {Delta} resultaria em estoque negativo (atual={Atual}) item={ItemId} produto={ProdId} loja={Loja}. Clampando para 0 e registrando divergência.",
                    deltaQty, atual, item.Id, produtoId, lojaId);
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

            // F9-F: audit criacao da movimentacao em movimentacao_estoque_alteracoes.
            // UsuarioId NOT NULL no schema; resolve "Sistema Mobile Sync" lazily.
            var sysUserId = await _systemUserResolver.GetOrCreateAsync(empresaId, ct);
            _db.Add(new MovimentacaoEstoqueAlteracao
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaId,
                MovimentacaoEstoqueId = movimentacao.Id,
                UsuarioId = sysUserId,
                NomeUsuario = "Sistema Mobile Sync",
                EmailUsuario = null,
                Acao = "criada",
                Motivo = $"Sync mobile · {natureza}",
                Observacao = descricao,
                AlteracoesJson = null,
                Ip = null,
                UserAgent = $"mobile_product={mobileProduct.Id}; doc={referenciaDocumento}",
                AlteradoEm = DateTime.UtcNow
            });

            // Espelha a quantidade no mobile_product (fonte da verdade local pro app)
            mobileProduct.Stock = (int)novo;
            mobileProduct.UpdatedAt = DateTime.UtcNow;

            // Marcador de divergência: registra MovimentacaoEstoque adicional
            // de natureza Ajuste com Quantidade=0 e Descricao indicando o evento.
            // Operador filtra por descrição "DIVERGENCIA_SYNC" pra investigar.
            if (divergiu)
            {
                _db.Add(new MovimentacaoEstoque
                {
                    Id = Guid.NewGuid(),
                    EmpresaId = empresaId,
                    ItemEstoqueId = item.Id,
                    ProdutoId = produtoId,
                    Tipo = TipoMovimentacaoEstoque.Saida,
                    Natureza = NaturezaMovimentacaoEstoque.Ajuste,
                    Quantidade = Quantidade.From(Math.Abs(deltaQty + atual)),
                    DataMovimentacao = DateTime.UtcNow,
                    Descricao = $"DIVERGENCIA_SYNC: mobile pediu delta {deltaQty} mas estoque atual era {atual}. Aplicado clamp pra 0. Doc={referenciaDocumento}",
                    DocumentoReferencia = referenciaDocumento,
                    CriadoEm = DateTime.UtcNow
                });
            }

            _log.LogInformation(
                "Reconciliação mobile->ERP {Status}: produto={ErpId} loja={LojaId} delta={Delta} novoEstoque={Novo} natureza={Nat} doc={Doc}",
                divergiu ? "com divergência" : "OK", produtoId, lojaId, deltaQty, novo, natureza, referenciaDocumento);
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

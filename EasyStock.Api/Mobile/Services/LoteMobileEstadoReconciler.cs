using EasyStock.Domain.Entities.Mobile;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Api.Mobile.Services;

/// <summary>
/// #1464 — leva ao ERP o estado de exclusão e descarte de um lote do PWA.
/// <para>
/// Age só nos itens de estoque do próprio lote (<c>CodigoInterno = lote:&lt;loteId&gt;:*</c>,
/// criados pelo <see cref="Linkers.BatchLinker"/>). Descarte = saída Perda do saldo restante;
/// exclusão = saída Ajuste do saldo restante. Ao mudar de estado, as saídas do estado
/// anterior são estornadas antes (desfazer descarte, restaurar lote). Mesmo estado = nada.
/// </para>
/// <para>
/// Unidades já vendidas não voltam nem saem de novo: só o saldo restante do lote muda.
/// </para>
/// </summary>
public class LoteMobileEstadoReconciler(
    EasyStockDbContext db,
    ILogger<LoteMobileEstadoReconciler> log)
{
    public const string StatusFinalizado = "finalizado";
    public const string StatusDescartado = "descartado";
    public const string StatusExcluido = "excluido";

    public static string StatusDesejado(Batch b) =>
        b.Excluido ? StatusExcluido : b.Descartado ? StatusDescartado : StatusFinalizado;

    private static string DocumentoDe(Guid loteId, string status) =>
        $"lote:{loteId}:{(status == StatusDescartado ? "descarte" : "exclusao")}";

    /// <summary>Aplica o estado do batch ao Lote ERP vinculado. Não chama SaveChanges.</summary>
    public async Task AplicarAsync(Batch batch, CancellationToken ct = default)
    {
        if (batch.ErpLoteId is not { } loteId || loteId == Guid.Empty) return;

        var lote = await db.Set<Lote>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(l => l.Id == loteId && l.EmpresaId == batch.EmpresaId, ct);
        if (lote == null) return;

        var desejado = StatusDesejado(batch);
        var atual = lote.Status is StatusDescartado or StatusExcluido ? lote.Status : StatusFinalizado;
        if (desejado == atual) return;

        var agora = DateTime.UtcNow;
        var prefixo = $"lote:{lote.Id}:";
        var itens = await db.Set<ItemEstoque>().IgnoreQueryFilters()
            .Where(i => i.EmpresaId == lote.EmpresaId && i.CodigoInterno != null && i.CodigoInterno.StartsWith(prefixo))
            .ToListAsync(ct);

        if (atual != StatusFinalizado)
            await EstornarSaidasAsync(lote, DocumentoDe(lote.Id, atual), itens, agora, ct);

        if (desejado != StatusFinalizado)
        {
            var natureza = desejado == StatusDescartado
                ? NaturezaMovimentacaoEstoque.Perda
                : NaturezaMovimentacaoEstoque.Ajuste;
            var motivo = desejado == StatusDescartado
                ? $"Descarte do lote {lote.Codigo}{(string.IsNullOrWhiteSpace(batch.DiscardReason) ? "" : " · " + batch.DiscardReason)}"
                : $"Exclusao do lote {lote.Codigo} (registrado por engano)";
            foreach (var item in itens)
            {
                var saldo = item.QuantidadeAtual?.Value ?? 0;
                if (saldo <= 0) continue;
                var qtd = Quantidade.From(saldo);
                item.QuantidadeAtual = Quantidade.Zero;
                item.UltimaMovimentacaoEm = agora;
                item.AlteradoEm = agora;
                item.RecalcularIndicadores(agora);
                db.Add(new MovimentacaoEstoque
                {
                    Id = Guid.NewGuid(),
                    EmpresaId = item.EmpresaId,
                    ItemEstoqueId = item.Id,
                    ProdutoId = item.ProdutoId,
                    ProdutoVariacaoId = item.ProdutoVariacaoId,
                    Tipo = TipoMovimentacaoEstoque.Saida,
                    Natureza = natureza,
                    Quantidade = qtd,
                    ValorUnitario = item.CustoUnitario,
                    ValorTotal = item.CustoUnitario != null
                        ? Dinheiro.FromDecimal(item.CustoUnitario.Valor * saldo)
                        : null,
                    DataMovimentacao = agora,
                    Descricao = motivo,
                    DocumentoReferencia = DocumentoDe(lote.Id, desejado),
                    CriadoEm = agora
                });
            }
        }

        lote.Status = desejado;
        lote.AlteradoEm = agora;
        log.LogInformation("Lote mobile {BatchId} → ERP {LoteId}: {De} → {Para}", batch.Id, lote.Id, atual, desejado);
    }

    private async Task EstornarSaidasAsync(
        Lote lote, string documento, List<ItemEstoque> itens, DateTime agora, CancellationToken ct)
    {
        var saidas = await db.Set<MovimentacaoEstoque>().IgnoreQueryFilters()
            .Where(m => m.EmpresaId == lote.EmpresaId
                        && m.DocumentoReferencia == documento
                        && m.Tipo == TipoMovimentacaoEstoque.Saida
                        && m.EstornadaEm == null)
            .ToListAsync(ct);
        foreach (var saida in saidas)
        {
            var item = itens.FirstOrDefault(i => i.Id == saida.ItemEstoqueId);
            item?.RestaurarQuantidade(saida.Quantidade, agora);
            saida.MarcarComoEstornada(agora);
            db.Add(MovimentacaoEstoque.CriarEstorno(
                Guid.NewGuid(), saida, agora,
                $"Estorno: lote {lote.Codigo} voltou a valer", agora));
        }
    }
}

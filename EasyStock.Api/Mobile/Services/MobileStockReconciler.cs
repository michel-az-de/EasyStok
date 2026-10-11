using EasyStock.Domain.Entities.Mobile;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Api.Mobile.Services;

/// <summary>
/// Leva ao estoque do ERP a venda de um pedido do PWA, quando o produto do aparelho está ligado a
/// um <see cref="Produto"/> (<c>ErpProductId</c>). Mesma regra da baixa do pedido no ERP
/// (<c>PedidoEstoqueIntegrationService</c>), sobre os lotes da loja do pedido (pedido sem loja: todos
/// os lotes da empresa, como no ERP):
/// <list type="bullet">
/// <item>sai primeiro do lote que vence antes (FEFO) e atravessa os lotes; bloqueado e descartado não baixam;</item>
/// <item>falta de saldo não trava: o saldo vai a zero e a falta vira <c>QuantidadeDescoberta</c> no lote (#540);</item>
/// <item>uma saída por lote, com o id do pedido em <c>DocumentoReferencia</c>; pedido e produto baixam uma vez só;</item>
/// <item>o cancelamento devolve o que este pedido baixou, aos lotes de onde saiu. Sem saída, não devolve nada.</item>
/// </list>
/// <para>
/// #1543: antes a baixa caía num lote qualquer, truncava o saldo em zero sem registrar a falta, e o
/// cancelamento somava a quantidade do pedido mesmo que nada tivesse saído por ele.
/// </para>
/// <para>
/// Não chama <c>SaveChanges</c>: as mudanças vão no commit de quem chamou (o lote do sync). Falha aqui
/// não derruba o sync do aparelho: é registrada e devolve <c>false</c>. Tudo é lido antes de qualquer
/// alteração, para a falha não deixar meia baixa no contexto.
/// </para>
/// </summary>
public class MobileStockReconciler(
    EasyStockDbContext db,
    MobileSystemUserResolver systemUserResolver,
    ILogger<MobileStockReconciler> log)
{
    /// <summary>Baixa a venda de um produto do pedido. Devolve false quando não há o que fazer (sem vínculo, sem lote, já baixado).</summary>
    public async Task<bool> BaixarVendaAsync(
        Product produtoDoAparelho, int quantidade, string pedidoId, Guid? lojaDoPedido, string descricao,
        CancellationToken ct = default)
    {
        try
        {
            if (Alvo(produtoDoAparelho) is not { } alvo || quantidade <= 0) return false;
            if ((await SaidasDoPedidoAsync(alvo, pedidoId, ct)).Count > 0) return false;

            var lotes = await LotesEmFefoAsync(alvo, lojaDoPedido, ct);
            if (lotes.Count == 0)
            {
                log.LogWarning(
                    "Venda do PWA sem lote no ERP: produto {ProdutoId} loja {LojaId} (produto do aparelho {MobileId}). Nada foi baixado.",
                    alvo.ProdutoId, lojaDoPedido, produtoDoAparelho.Id);
                return false;
            }
            var usuarioSistema = await systemUserResolver.GetOrCreateAsync(alvo.EmpresaId, ct);

            var agora = DateTime.UtcNow;
            var restante = (decimal)quantidade;
            var saidas = new List<(ItemEstoque Lote, decimal Quantidade)>();
            foreach (var lote in lotes.Where(l => Saldo(l) > 0m))
            {
                if (restante <= 0m) break;
                var consumir = Math.Min(restante, Saldo(lote));
                lote.RegistrarSaida(Quantidade.From(consumir), agora, agora, permitirVencido: true);
                saidas.Add((lote, consumir));
                restante -= consumir;
            }

            var falta = restante;
            if (falta > 0m)
            {
                // A falta fica no ultimo lote que baixou; sem nenhum com saldo, no que vence primeiro.
                var comFalta = saidas.Count > 0 ? saidas[^1].Lote : lotes[0];
                comFalta.RegistrarSaidaPermitindoDescoberto(Quantidade.From(falta), agora, agora, permitirVencido: true);
                var indice = saidas.FindIndex(s => s.Lote.Id == comFalta.Id);
                if (indice >= 0) saidas[indice] = (comFalta, saidas[indice].Quantidade + falta);
                else saidas.Add((comFalta, falta));
                log.LogWarning(
                    "Venda do PWA sem saldo: pedido {PedidoId} produto {ProdutoId} pediu {Quantidade} e faltaram {Falta}. Ficou a descoberto no lote {LoteId}.",
                    pedidoId, alvo.ProdutoId, quantidade, falta, comFalta.Id);
            }

            foreach (var (lote, quantidadeDoLote) in saidas)
            {
                var temFalta = falta > 0m && lote.Id == saidas[^1].Lote.Id;
                Registrar(new MovimentacaoEstoque
                {
                    Id = Guid.NewGuid(),
                    EmpresaId = alvo.EmpresaId,
                    ItemEstoqueId = lote.Id,
                    ProdutoId = alvo.ProdutoId,
                    ProdutoVariacaoId = lote.ProdutoVariacaoId,
                    Tipo = TipoMovimentacaoEstoque.Saida,
                    Natureza = NaturezaMovimentacaoEstoque.Venda,
                    Quantidade = Quantidade.From(quantidadeDoLote),
                    ValorUnitario = lote.CustoUnitario,
                    ValorTotal = lote.CustoUnitario != null
                        ? Dinheiro.FromDecimal(lote.CustoUnitario.Valor * quantidadeDoLote)
                        : null,
                    DataMovimentacao = agora,
                    Descricao = temFalta ? $"{descricao} · {falta:0.###} a descoberto" : descricao,
                    DocumentoReferencia = pedidoId,
                    CriadoEm = agora
                }, usuarioSistema, produtoDoAparelho, agora);
            }
            return true;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao baixar no ERP a venda do pedido {PedidoId} (produto do aparelho {MobileId}). O sync segue.",
                pedidoId, produtoDoAparelho.Id);
            return false;
        }
    }

    /// <summary>Devolve o que este pedido baixou deste produto. Devolve false quando não havia saída a devolver.</summary>
    public async Task<bool> DevolverVendaAsync(
        Product produtoDoAparelho, string pedidoId, string descricao, CancellationToken ct = default)
    {
        try
        {
            if (Alvo(produtoDoAparelho) is not { } alvo) return false;
            var saidas = await SaidasDoPedidoAsync(alvo, pedidoId, ct);
            if (saidas.Count == 0) return false;

            var idsDosLotes = saidas.Select(s => s.ItemEstoqueId).Distinct().ToList();
            var lotes = await db.Set<ItemEstoque>().IgnoreQueryFilters()
                .Where(i => i.EmpresaId == alvo.EmpresaId && idsDosLotes.Contains(i.Id))
                .ToDictionaryAsync(i => i.Id, ct);
            var usuarioSistema = await systemUserResolver.GetOrCreateAsync(alvo.EmpresaId, ct);

            var agora = DateTime.UtcNow;
            foreach (var saida in saidas)
            {
                // A parte que ficou a descoberto nunca saiu do lote: e abatida antes de voltar ao saldo.
                if (lotes.TryGetValue(saida.ItemEstoqueId, out var lote))
                    lote.RestaurarSaidaEstornada(saida.Quantidade, agora);
                saida.MarcarComoEstornada(agora);
                Registrar(MovimentacaoEstoque.CriarEstorno(Guid.NewGuid(), saida, agora, descricao, agora),
                    usuarioSistema, produtoDoAparelho, agora);
            }
            return true;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao devolver ao ERP a venda do pedido {PedidoId} (produto do aparelho {MobileId}). O sync segue.",
                pedidoId, produtoDoAparelho.Id);
            return false;
        }
    }

    /// <summary>
    /// Junta as linhas do pedido pelo produto do ERP: duas linhas do mesmo produto, ou dois produtos do
    /// aparelho ligados ao mesmo produto, são uma baixa só. Produto sem vínculo fica de fora.
    /// </summary>
    public static IEnumerable<(Product Produto, int Quantidade)> PorProdutoDoErp(
        IEnumerable<(Product Produto, int Quantidade)> linhas) =>
        linhas.Where(l => l.Produto.ErpProductId.HasValue)
            .GroupBy(l => l.Produto.ErpProductId)
            .Select(g => (g.First().Produto, g.Sum(l => l.Quantidade)));

    private sealed record AlvoNoErp(Guid EmpresaId, Guid ProdutoId);

    // Sem vinculo com o ERP, ou aparelho sem empresa: a venda fica so no espelho do aparelho.
    private static AlvoNoErp? Alvo(Product p) =>
        p is { ErpProductId: { } produtoId, EmpresaId: { } empresaId } ? new AlvoNoErp(empresaId, produtoId) : null;

    private static decimal Saldo(ItemEstoque lote) => lote.QuantidadeAtual?.Value ?? 0m;

    // Lotes da loja do pedido, para a venda de uma loja nao consumir o estoque de outra; pedido sem
    // loja enxerga todos os lotes da empresa (mesma regra de PedidoEstoqueIntegrationService, #1534).
    // A loja e a do pedido, nao a de quem cadastrou o produto. IgnoreQueryFilters: o sync do aparelho
    // nao tem JWT; o isolamento e o filtro por empresa, que vem do proprio aparelho.
    private async Task<List<ItemEstoque>> LotesEmFefoAsync(AlvoNoErp alvo, Guid? lojaDoPedido, CancellationToken ct)
    {
        var lotes = await db.Set<ItemEstoque>().IgnoreQueryFilters()
            .Where(i => i.EmpresaId == alvo.EmpresaId && i.ProdutoId == alvo.ProdutoId
                        && (lojaDoPedido == null || i.LojaId == lojaDoPedido)
                        && i.Status != StatusItemEstoque.Bloqueado && i.Status != StatusItemEstoque.Descartado)
            .ToListAsync(ct);
        return lotes
            .OrderBy(i => i.ValidadeEm ?? DateTime.MaxValue)
            .ThenBy(i => i.EntradaEm)
            .ThenBy(i => i.CriadoEm)
            .ToList();
    }

    // Saidas de venda deste pedido e produto ainda nao estornadas. Olha tambem o que foi registrado
    // neste mesmo lote do sync e ainda nao foi gravado.
    private async Task<List<MovimentacaoEstoque>> SaidasDoPedidoAsync(AlvoNoErp alvo, string pedidoId, CancellationToken ct)
    {
        bool DoPedido(MovimentacaoEstoque m) =>
            m.EmpresaId == alvo.EmpresaId && m.ProdutoId == alvo.ProdutoId && m.DocumentoReferencia == pedidoId
            && m.Tipo == TipoMovimentacaoEstoque.Saida && m.Natureza == NaturezaMovimentacaoEstoque.Venda
            && m.EstornadaEm == null;

        var gravadas = await db.Set<MovimentacaoEstoque>().IgnoreQueryFilters()
            .Where(m => m.EmpresaId == alvo.EmpresaId && m.ProdutoId == alvo.ProdutoId && m.DocumentoReferencia == pedidoId
                        && m.Tipo == TipoMovimentacaoEstoque.Saida && m.Natureza == NaturezaMovimentacaoEstoque.Venda
                        && m.EstornadaEm == null)
            .ToListAsync(ct);
        var doLote = db.ChangeTracker.Entries<MovimentacaoEstoque>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity)
            .Where(DoPedido);
        // Uma saida gravada pode ter sido estornada neste mesmo lote (ainda sem commit): DoPedido relê o estado atual.
        return gravadas.Where(DoPedido).Concat(doLote).DistinctBy(m => m.Id).ToList();
    }

    // F9-F: toda movimentacao criada pelo sync leva a trilha em movimentacao_estoque_alteracoes.
    private void Registrar(MovimentacaoEstoque movimentacao, Guid usuarioSistema, Product produtoDoAparelho, DateTime agora)
    {
        db.Add(movimentacao);
        db.Add(new MovimentacaoEstoqueAlteracao
        {
            Id = Guid.NewGuid(),
            EmpresaId = movimentacao.EmpresaId,
            MovimentacaoEstoqueId = movimentacao.Id,
            UsuarioId = usuarioSistema,
            NomeUsuario = "Sistema Mobile Sync",
            EmailUsuario = null,
            Acao = "criada",
            Motivo = $"Sync mobile · {movimentacao.Natureza}",
            Observacao = movimentacao.Descricao,
            AlteracoesJson = null,
            Ip = null,
            UserAgent = $"mobile_product={produtoDoAparelho.Id}; doc={movimentacao.DocumentoReferencia}",
            AlteradoEm = agora
        });
    }
}

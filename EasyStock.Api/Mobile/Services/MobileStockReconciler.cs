using EasyStock.Application.Services;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Domain.Services;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;

namespace EasyStock.Api.Mobile.Services;

/// <summary>
/// Leva ao estoque do ERP a venda de um pedido do PWA, quando o produto do aparelho está ligado a
/// um <see cref="Produto"/> (<c>ErpProductId</c>). Mesma regra da baixa do pedido no ERP
/// (<c>PedidoEstoqueIntegrationService</c>), sobre os lotes da loja do pedido (pedido sem loja: todos
/// os lotes da empresa, como no ERP):
/// <list type="bullet">
/// <item>escolhe os lotes pela regra única <see cref="ConsumoDeLotes"/> (#2003): FEFO, vencido por último, empate pelo
/// lote que entrou antes e <c>FifoAtivo</c> da loja; atravessa os lotes; bloqueado e descartado não baixam;</item>
/// <item>falta de saldo não trava: o saldo vai a zero e a falta vira <c>QuantidadeDescoberta</c> no lote (#540);</item>
/// <item>uma saída por lote, com o id do pedido em <c>DocumentoReferencia</c>; pedido e produto baixam uma vez só;</item>
/// <item>o cancelamento devolve o que este pedido baixou, aos lotes de onde saiu. Sem saída, não devolve nada.</item>
/// </list>
/// <para>
/// #1571: o pedido que nasceu no aparelho vira Pedido do ERP, e o console ou o KDS pode avançá-lo. Aí
/// a esteira baixa com outro documento (<c>{pedido}:{item}</c>). É a mesma venda com duas chaves: cada
/// lado baixa só o que o outro ainda não baixou daquele produto, e o cancelamento, venha de onde vier,
/// devolve o que está aberto pelas duas. A outra metade da regra está em <see cref="PedidoEstoqueIntegrationService"/>.
/// </para>
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
    /// <param name="pedidoDoErp">
    /// Pedido do ERP criado a partir deste pedido do aparelho, quando já existe. #1571: o console ou o
    /// KDS pode tê-lo avançado, e a esteira baixa com outro documento (<c>{pedido}:{item}</c>). O que
    /// ela já baixou deste produto não sai de novo; sai só a diferença.
    /// </param>
    public async Task<bool> BaixarVendaAsync(
        Product produtoDoAparelho, int quantidade, string pedidoId, Guid? lojaDoPedido, string descricao,
        Guid? pedidoDoErp = null, CancellationToken ct = default)
    {
        try
        {
            if (Alvo(produtoDoAparelho) is not { } alvo || quantidade <= 0) return false;
            if ((await SaidasAbertasAsync(alvo.EmpresaId, alvo.ProdutoId, pedidoId, null, ct)).Count > 0) return false;

            var aBaixar = (decimal)quantidade;
            if (pedidoDoErp is { } doErp)
                aBaixar -= (await SaidasAbertasAsync(alvo.EmpresaId, alvo.ProdutoId, null, doErp, ct)).Sum(s => s.Quantidade.Value);
            if (aBaixar <= 0m) return false;

            var lotes = await LotesDaLojaAsync(alvo, lojaDoPedido, ct);
            if (lotes.Count == 0)
            {
                log.LogWarning(
                    "Venda do PWA sem lote no ERP: produto {ProdutoId} loja {LojaId} (produto do aparelho {MobileId}). Nada foi baixado.",
                    alvo.ProdutoId, lojaDoPedido, produtoDoAparelho.Id);
                return false;
            }
            var usuarioSistema = await systemUserResolver.GetOrCreateAsync(alvo.EmpresaId, ct);

            var agora = DateTime.UtcNow;
            var plano = ConsumoDeLotes.Planejar(lotes, aBaixar, agora, await RegraDeConsumoAsync(alvo.EmpresaId, lojaDoPedido, ct));
            // A falta fica no ultimo lote que baixou; sem nenhum com saldo, no primeiro da ordem.
            plano.Aplicar(agora, agora, permitirVencido: true);
            if (plano.Falta > 0m)
                log.LogWarning(
                    "Venda do PWA sem saldo: pedido {PedidoId} produto {ProdutoId} pediu {Quantidade} e faltaram {Falta}. Ficou a descoberto no lote {LoteId}.",
                    pedidoId, alvo.ProdutoId, aBaixar, plano.Falta, plano.Consumos[^1].Lote.Id);

            foreach (var (lote, quantidadeDoLote, descoberto) in plano.Consumos)
            {
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
                    Descricao = descoberto > 0m ? $"{descricao} · {descoberto:0.###} a descoberto" : descricao,
                    DocumentoReferencia = pedidoId,
                    CriadoEm = agora
                }, usuarioSistema, $"mobile_product={produtoDoAparelho.Id}", agora);
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

    /// <summary>
    /// Devolve tudo o que este pedido baixou e ainda vale, de qualquer produto (inclusive o do item que
    /// saiu do pedido depois de baixar). Devolve false quando não havia saída a devolver.
    /// </summary>
    /// <param name="pedidoDoErp">
    /// Só no pedido que <b>nasceu no aparelho</b>: o Pedido do ERP criado a partir dele. #1571: devolve
    /// também o que a esteira baixou por esse pedido (o console avançou antes). No pedido que nasceu no
    /// ERP fica nulo: lá quem devolve é o cancelamento da esteira, que pode recusar.
    /// </param>
    public async Task<bool> DevolverVendaAsync(
        Guid empresaId, string pedidoId, string descricao, Guid? pedidoDoErp = null, CancellationToken ct = default)
    {
        try
        {
            var saidas = await SaidasAbertasAsync(empresaId, null, pedidoId, pedidoDoErp, ct);
            if (saidas.Count == 0) return false;

            var idsDosLotes = saidas.Select(s => s.ItemEstoqueId).Distinct().ToList();
            var lotes = await db.Set<ItemEstoque>().IgnoreQueryFilters()
                .Where(i => i.EmpresaId == empresaId && idsDosLotes.Contains(i.Id))
                .ToDictionaryAsync(i => i.Id, ct);
            var usuarioSistema = await systemUserResolver.GetOrCreateAsync(empresaId, ct);

            var agora = DateTime.UtcNow;
            foreach (var saida in saidas)
            {
                // A parte que ficou a descoberto nunca saiu do lote: e abatida antes de voltar ao saldo.
                lotes.TryGetValue(saida.ItemEstoqueId, out var lote);
                Registrar(
                    DevolucaoDeSaida.Aplicar(saida, lote, agora, descricao,
                        PedidoEstoqueIntegrationService.MotivoDoEstornoPorCancelamento),
                    usuarioSistema, $"mobile_order={pedidoId}", agora);
            }
            return true;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao devolver ao ERP a venda do pedido {PedidoId}. O sync segue.", pedidoId);
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

    // Lotes da loja do pedido, para a venda de uma loja nao consumir o estoque de outra; pedido sem
    // loja enxerga todos os lotes da empresa (mesma regra de PedidoEstoqueIntegrationService, #1534).
    // A loja e a do pedido, nao a de quem cadastrou o produto. IgnoreQueryFilters: o sync do aparelho
    // nao tem JWT; o isolamento e o filtro por empresa, que vem do proprio aparelho. A ordem de consumo
    // e de ConsumoDeLotes (#2003), nao daqui.
    private async Task<List<ItemEstoque>> LotesDaLojaAsync(AlvoNoErp alvo, Guid? lojaDoPedido, CancellationToken ct) =>
        await db.Set<ItemEstoque>().IgnoreQueryFilters()
            .Where(i => i.EmpresaId == alvo.EmpresaId && i.ProdutoId == alvo.ProdutoId
                        && (lojaDoPedido == null || i.LojaId == lojaDoPedido)
                        && i.Status != StatusItemEstoque.Bloqueado && i.Status != StatusItemEstoque.Descartado)
            .ToListAsync(ct);

    // FifoAtivo da loja do pedido (ou da loja unica da empresa, pedido sem loja). Sem configuracao, FEFO.
    private async Task<RegraDeConsumo> RegraDeConsumoAsync(Guid empresaId, Guid? lojaDoPedido, CancellationToken ct) =>
        new(Fefo: (await ConfiguracaoLojaRepository.DaLojaAsync(db.Set<ConfiguracaoLoja>(), empresaId, lojaDoPedido, ct))?.FifoAtivo ?? true);

    // Saidas de venda ainda nao estornadas de um pedido: as do aparelho (documento = id do pedido de
    // la), as da esteira do ERP (documento = "{pedido do ERP}:{item}"), ou as duas. produtoId nulo =
    // de qualquer produto. Olha tambem o que foi registrado neste mesmo lote do sync e ainda nao foi gravado.
    private async Task<List<MovimentacaoEstoque>> SaidasAbertasAsync(
        Guid empresaId, Guid? produtoId, string? pedidoDoAparelho, Guid? pedidoDoErp, CancellationToken ct)
    {
        var daEsteira = pedidoDoErp is { } doErp ? $"{doErp}:" : null;
        bool DoPedido(MovimentacaoEstoque m) =>
            m.EmpresaId == empresaId && (produtoId == null || m.ProdutoId == produtoId)
            && m.DocumentoReferencia != null
            && ((pedidoDoAparelho != null && m.DocumentoReferencia == pedidoDoAparelho)
                || (daEsteira != null && m.DocumentoReferencia.StartsWith(daEsteira, StringComparison.Ordinal)))
            && m.Tipo == TipoMovimentacaoEstoque.Saida && m.Natureza == NaturezaMovimentacaoEstoque.Venda
            && m.EstornadaEm == null;

        var gravadas = await db.Set<MovimentacaoEstoque>().IgnoreQueryFilters()
            .Where(m => m.EmpresaId == empresaId && (produtoId == null || m.ProdutoId == produtoId)
                        && ((pedidoDoAparelho != null && m.DocumentoReferencia == pedidoDoAparelho)
                            || (daEsteira != null && m.DocumentoReferencia!.StartsWith(daEsteira)))
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
    private void Registrar(MovimentacaoEstoque movimentacao, Guid usuarioSistema, string origem, DateTime agora)
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
            UserAgent = $"{origem}; doc={movimentacao.DocumentoReferencia}",
            AlteradoEm = agora
        });
    }
}

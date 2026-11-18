using System.Text.Json;
using EasyStock.Application.Services.Pedidos;
using EasyStock.Application.Services.Storefront;
using EasyStock.Api.Mobile.DTOs;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Domain.Sales;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Api.Mobile.Services;

/// <summary>
/// F6 — Acrescenta na resposta do pull as entidades criadas/editadas no web
/// (Pedido/Produto/Cliente/Lote/MovimentoCaixa) que ainda não têm espelho mobile.
/// Extracted from SyncController to keep the HTTP layer thin.
/// </summary>
public class SyncReversePullService(
    EasyStockDbContext db,
    IConfiguration config,
    ILogger<SyncReversePullService> log,
    NumeroCurtoDosLotesMobile numerosCurtos,
    SaldoFinanceiroPedido? saldos = null)
{
    private readonly SaldoFinanceiroPedido? _saldos = saldos;
    private readonly NumeroCurtoDosLotesMobile _numerosCurtos = numerosCurtos;
    private readonly EasyStockDbContext _db = db;
    private readonly IConfiguration _config = config;
    private readonly ILogger<SyncReversePullService> _log = log;

    /// <summary>
    /// #2813 (B5): janela, em dias, do histórico que desce na PRIMEIRA carga do aparelho (pull sem cursor).
    /// Ponto de partida, fácil de mudar: o aparelho tem cota pequena de localStorage e não precisa do
    /// histórico inteiro. O histórico completo continua no ERP e no Console; o filtro só restringe o que
    /// este pull devolve, não apaga nada do servidor nem do aparelho. O pull incremental (cursor maior
    /// que zero) não usa a janela. O pedido entregue conta a janela da data da entrega (EntreguEm, ou CriadoEm
    /// quando nula) e, enquanto tiver saldo a receber, desce de qualquer idade: dinheiro a receber não some da tela.
    /// </summary>
    public const int JanelaPrimeiraCargaDias = 30;

    /// <summary>
    /// #1469: o grid de producao do PWA so desenha massa/molho/extra. Traduz a Categoria ERP
    /// pelo nome; o que nao casar vai para extra, para nunca sumir da tela.
    /// </summary>
    internal static string CategoriaDoPwa(string? nomeCategoriaErp)
    {
        var nome = (nomeCategoriaErp ?? string.Empty).Trim().ToLowerInvariant();
        if (nome.Contains("massa")) return "massa";
        if (nome.Contains("molho")) return "molho";
        return "extra";
    }

    /// <summary>
    /// CLI-3 (#2321): Cliente do ERP editado no console que ja tem espelho em mobile_clients. O bloco
    /// acima os exclui; aqui o espelho recebe a edicao (nome, contato, endereco e documento) com o Id
    /// do app. Se o pull normal ja mandou um client.upsert desse id, a mutacao e mesclada, nunca duplicada.
    /// Campo vazio no ERP nao apaga o dado do app.
    /// </summary>
    private async Task AppendClientesEspelhadosAsync(List<MutationDto> mutations, DateTime sinceDate, Guid empresaId, Guid? lojaId)
    {
        var espelhados = await (
            from m in _db.Set<Client>().IgnoreQueryFilters().AsNoTracking()
            join c in _db.Set<Cliente>().IgnoreQueryFilters().AsNoTracking() on m.ErpClienteId equals c.Id
            where m.EmpresaId == empresaId && c.EmpresaId == empresaId && c.Ativo && c.AlteradoEm > sinceDate
                && (!lojaId.HasValue || m.LojaId == lojaId || m.LojaId == null)
            select new { Espelho = m, Erp = c }).ToListAsync();
        foreach (var par in espelhados)
        {
            var (m, c) = (par.Espelho, par.Erp);
            var indice = mutations.FindIndex(x => x.Type == "client.upsert"
                && x.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
                && x.Payload.TryGetProperty("id", out var id) && id.GetString() == m.Id);
            var dto = indice >= 0
                ? System.Text.Json.JsonSerializer.Deserialize<ClientDto>(mutations[indice].Payload, SyncDtoConverters.JsonOpts)!
                : SyncDtoConverters.ToDto(m);
            dto = dto with
            {
                Name = string.IsNullOrWhiteSpace(c.Nome) ? dto.Name : c.Nome,
                Apt = string.IsNullOrWhiteSpace(c.Apt) ? dto.Apt : c.Apt,
                Address = string.IsNullOrWhiteSpace(c.Endereco) ? dto.Address : c.Endereco,
                Phone = string.IsNullOrWhiteSpace(c.Telefone) ? dto.Phone : c.Telefone,
                Document = string.IsNullOrWhiteSpace(c.Documento) ? dto.Document : c.Documento
            };
            var ts = new DateTimeOffset(c.AlteradoEm).ToUnixTimeMilliseconds();
            if (indice >= 0)
                mutations[indice] = mutations[indice] with { Payload = SyncDtoConverters.Serialize(dto), Ts = Math.Max(mutations[indice].Ts, ts) };
            else
                mutations.Add(new MutationDto(Guid.NewGuid().ToString(), "web", "client.upsert", SyncDtoConverters.Serialize(dto), ts));
        }
    }

    private static long UltimaMudanca(MovimentoCaixa m) => new DateTimeOffset(
        m.EstornadoEm is { } estornadoEm && estornadoEm > m.CriadoEm ? estornadoEm : m.CriadoEm).ToUnixTimeMilliseconds();

    /// <summary>
    /// #2731: lancamento do aparelho estornado no console. O pull de mobile_cash_entries ja pode ter
    /// trazido o mesmo id (outro aparelho editou, ou o proprio aparelho excluiu): marca estornado
    /// nele em vez de mandar um segundo upsert.
    /// </summary>
    private static void EstornoDoLancamentoDoAparelho(List<MutationDto> mutations, MovimentoCaixa m)
    {
        var idDoAparelho = m.Referencia!["mobile:".Length..];
        var idx = mutations.FindIndex(x => x.Type == "cashEntry.upsert"
            && x.Payload.TryGetProperty("id", out var id) && id.GetString() == idDoAparelho);
        if (idx >= 0)
        {
            var doApp = mutations[idx].Payload.Deserialize<CashEntryDto>(SyncDtoConverters.JsonOpts);
            if (doApp is not null)
                mutations[idx] = mutations[idx] with { Payload = SyncDtoConverters.Serialize(doApp with { Estornado = true }) };
            return;
        }
        var dto = new CashEntryDto(
            Id: idDoAparelho,
            Type: m.Tipo is "entrada" or "abertura" ? "income" : "expense",
            Amount: m.Valor,
            Description: m.Descricao ?? "",
            CreatedAt: new DateTimeOffset(m.DataMovimento).ToUnixTimeMilliseconds(),
            Estornado: true,
            Metodo: m.Metodo);
        mutations.Add(new MutationDto(Guid.NewGuid().ToString(), "web",
            "cashEntry.upsert", SyncDtoConverters.Serialize(dto), UltimaMudanca(m)));
    }

    private async Task<Dictionary<Guid, decimal>> SaldoNaLojaAsync(List<Guid> produtoIds, Guid empresaId, Guid? lojaId)
    {
        if (produtoIds.Count == 0) return [];
        var q = _db.Set<ItemEstoque>().IgnoreQueryFilters().AsNoTracking()
            .Where(i => i.EmpresaId == empresaId && produtoIds.Contains(i.ProdutoId));
        if (lojaId.HasValue) q = q.Where(i => i.LojaId == lojaId || i.LojaId == null);
        var itens = await q.ToListAsync();
        return itens.GroupBy(i => i.ProdutoId).ToDictionary(g => g.Key, g => g.Sum(i => i.QuantidadeAtual?.Value ?? 0m));
    }

    public async Task AppendAsync(List<MutationDto> mutations, DateTime sinceDate, Guid empresaId, Guid? lojaId)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // #2813 (B5): primeira carga = aparelho sem cursor (o SyncController entrega UnixEpoch quando since é 0).
        // Só nela o pull deixa o histórico no ERP. Com cursor o aparelho pode ter o registro e precisa saber do
        // cancelamento e do estorno, então o incremental segue devolvendo tudo o que mudou. Cliente e produto
        // não são filtrados. O filtro só tira linhas da resposta: não existe caminho novo de delete.
        var primeiraCarga = sinceDate <= DateTime.UnixEpoch;
        var inicioDaJanela = DateTime.UtcNow.AddDays(-JanelaPrimeiraCargaDias);

        // Produtos web sem mobile_product correspondente (Erp link).
        var mobileLinkedProdutos = await _db.Set<Product>().IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.EmpresaId == empresaId && p.ErpProductId != null)
            .Select(p => p.ErpProductId!.Value).ToListAsync();
        var produtosQ = _db.Set<Produto>().IgnoreQueryFilters().AsNoTracking()
            .Include(p => p.Categoria)
            .Where(p => p.EmpresaId == empresaId && p.AlteradoEm > sinceDate
                && p.Status == StatusProduto.Ativo
                && !mobileLinkedProdutos.Contains(p.Id));
        var produtos = await produtosQ.ToListAsync();
        // #2083 (A7): o produto novo desce com o saldo do ERP na loja do aparelho (e o sem loja), não com 0.
        var saldoNovos = await SaldoNaLojaAsync(produtos.Select(p => p.Id).ToList(), empresaId, lojaId);
        foreach (var p in produtos)
        {
            var dto = new ProductDto(
                Id: p.Id.ToString(),
                Name: p.Nome,
                Emoji: null,
                Category: CategoriaDoPwa(p.Categoria?.Nome),
                Unit: null,
                Price: p.PrecoReferencia?.Valor ?? 0m,
                Stock: saldoNovos.GetValueOrDefault(p.Id),
                Custom: false,
                Sku: p.CodigoBarras,
                DefaultWeightG: null,
                DefaultValidityDays: null,
                Cost: p.CustoReferencia?.Valor,
                MinStock: p.QuantidadeMinima);
            mutations.Add(new MutationDto(Guid.NewGuid().ToString(), "web",
                "product.upsert", SyncDtoConverters.Serialize(dto), new DateTimeOffset(p.AlteradoEm).ToUnixTimeMilliseconds()));
        }

        // SYNC-2/SYNC-1 (#2306): Produto ERP ja espelhado e alterado depois do cursor.
        // Reenvia o cadastro com o Id do espelho e o saldo de itens_estoque da loja do device.
        // #2721: entrada ou venda no console muda so o ItemEstoque, nao o Produto; o estoque da loja
        // do device movido depois do cursor tambem reenvia. EXISTS na mesma consulta, sem N+1.
        var estoqueMovido = _db.Set<ItemEstoque>()
            .Where(i => i.EmpresaId == empresaId && i.AlteradoEm > sinceDate);
        if (lojaId.HasValue) estoqueMovido = estoqueMovido.Where(i => i.LojaId == lojaId || i.LojaId == null);
        var produtosEspelhados = await _db.Set<Produto>().IgnoreQueryFilters().AsNoTracking()
            .Include(p => p.Categoria)
            .Where(p => p.EmpresaId == empresaId
                && p.Status == StatusProduto.Ativo
                && mobileLinkedProdutos.Contains(p.Id)
                && (p.AlteradoEm > sinceDate || estoqueMovido.Any(i => i.ProdutoId == p.Id)))
            .ToListAsync();
        if (produtosEspelhados.Count > 0)
        {
            var idsErp = produtosEspelhados.Select(p => p.Id).ToList();
            var espelhosQ = _db.Set<Product>().IgnoreQueryFilters().AsNoTracking()
                .Where(m => m.EmpresaId == empresaId && m.ErpProductId != null && idsErp.Contains(m.ErpProductId!.Value));
            if (lojaId.HasValue) espelhosQ = espelhosQ.Where(m => m.LojaId == lojaId || m.LojaId == null);
            var espelhos = (await espelhosQ.ToListAsync()).ToLookup(m => m.ErpProductId!.Value);

            var estoqueQ = _db.Set<ItemEstoque>().IgnoreQueryFilters().AsNoTracking()
                .Where(i => i.EmpresaId == empresaId && idsErp.Contains(i.ProdutoId));
            if (lojaId.HasValue) estoqueQ = estoqueQ.Where(i => i.LojaId == lojaId || i.LojaId == null);
            var itensEstoque = await estoqueQ.ToListAsync();

            foreach (var p in produtosEspelhados)
            foreach (var espelho in espelhos[p.Id])
            {
                var itensDoProduto = itensEstoque.Where(i => i.ProdutoId == p.Id).ToList();
                // Sem estoque ERP, o produto criado no app conserva seu saldo, inclusive no pull do criador.
                // #2440: alterar so o cadastro (nome, preco) nao troca o saldo do app; o saldo do ERP
                // so vai quando algum item de estoque mudou depois do cursor (AlteradoEm e do servidor).
                var estoqueErpMudou = itensDoProduto.Any(i => i.AlteradoEm > sinceDate);
                var saldo = itensDoProduto.Count > 0 && estoqueErpMudou
                    ? itensDoProduto.Sum(i => i.QuantidadeAtual?.Value ?? 0m) : espelho.Stock;
                var dto = new ProductDto(
                    Id: espelho.Id,
                    Name: p.Nome,
                    Emoji: espelho.Emoji,
                    Category: CategoriaDoPwa(p.Categoria?.Nome),
                    Unit: espelho.Unit,
                    Price: p.PrecoReferencia?.Valor ?? 0m,
                    Stock: saldo,
                    Custom: espelho.IsCustom,
                    Sku: p.CodigoBarras,
                    DefaultWeightG: espelho.DefaultWeightG,
                    DefaultValidityDays: espelho.DefaultValidityDays,
                    Cost: p.CustoReferencia?.Valor,
                    MinStock: p.QuantidadeMinima);
                // O pull de mobile_products ja trouxe este espelho: mescla o cadastro do ERP
                // e mantem o estoque do app, para nao mandar dois upserts do mesmo produto.
                var idx = mutations.FindIndex(m => m.Type == "product.upsert"
                    && m.Payload.TryGetProperty("id", out var id) && id.GetString() == espelho.Id);
                if (idx >= 0)
                {
                    var doApp = mutations[idx].Payload.Deserialize<ProductDto>(SyncDtoConverters.JsonOpts);
                    if (doApp is not null) dto = dto with { Stock = doApp.Stock };
                    mutations[idx] = mutations[idx] with { Payload = SyncDtoConverters.Serialize(dto) };
                    continue;
                }
                // #2721: o instante e o da ultima mudanca, do cadastro ou do estoque da loja.
                var alteradoEm = itensDoProduto.Select(i => i.AlteradoEm).Append(p.AlteradoEm).Max();
                mutations.Add(new MutationDto(Guid.NewGuid().ToString(), "web",
                    "product.upsert", SyncDtoConverters.Serialize(dto), new DateTimeOffset(alteradoEm).ToUnixTimeMilliseconds()));
            }
        }

        // SYNC-3 (#2316): produto inativado no ERP depois do cursor sai do app. O PWA ja
        // entende "product.delete" (applyServerMutations); o id e o do espelho quando existe.
        // Cursor zerado (instalacao nova): o aparelho nao tem o que remover, entao nao reemite o
        // delete de todos os inativos historicos. Aparelho com cache e cursor zerado e caso aceito:
        // o upsert do inativo ja e omitido (SyncController), e o splice do delete e idempotente.
        var inativos = await _db.Set<Produto>().IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.EmpresaId == empresaId && p.AlteradoEm > sinceDate
                && sinceDate > DateTime.UnixEpoch
                && p.Status != StatusProduto.Ativo)
            .Select(p => new { p.Id, p.AlteradoEm })
            .ToListAsync();
        if (inativos.Count > 0)
        {
            var idsInativos = inativos.Select(p => p.Id).ToList();
            var espelhosInativos = (await _db.Set<Product>().IgnoreQueryFilters().AsNoTracking()
                .Where(m => m.EmpresaId == empresaId && m.ErpProductId != null && idsInativos.Contains(m.ErpProductId!.Value)
                    && (!lojaId.HasValue || m.LojaId == lojaId || m.LojaId == null))
                .Select(m => new { ErpId = m.ErpProductId!.Value, m.Id })
                .ToListAsync())
                .GroupBy(m => m.ErpId).ToDictionary(g => g.Key, g => g.Select(m => m.Id).ToList());
            foreach (var p in inativos)
            {
                var ids = espelhosInativos.TryGetValue(p.Id, out var espelhos) ? espelhos : new List<string> { p.Id.ToString() };
                foreach (var idApp in ids)
                    mutations.Add(new MutationDto(Guid.NewGuid().ToString(), "web",
                        "product.delete", SyncDtoConverters.Serialize(new { id = idApp }),
                        new DateTimeOffset(p.AlteradoEm).ToUnixTimeMilliseconds()));
            }
        }

        // Clientes web sem mobile_client correspondente.
        var mobileLinkedClientes = await _db.Set<Client>().IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.EmpresaId == empresaId && c.ErpClienteId != null)
            .Select(c => c.ErpClienteId!.Value).ToListAsync();
        var clientesQ = _db.Set<Cliente>().IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.EmpresaId == empresaId && c.AlteradoEm > sinceDate
                && c.Ativo && !mobileLinkedClientes.Contains(c.Id));
        var clientes = await clientesQ.ToListAsync();
        foreach (var c in clientes)
        {
            var dto = new ClientDto(
                Id: c.Id.ToString(),
                Name: c.Nome,
                Apt: c.Apt,
                Address: c.Endereco,
                Phone: c.Telefone,
                LastOrder: c.LastOrderAt.HasValue ? new DateTimeOffset(c.LastOrderAt.Value).ToUnixTimeMilliseconds() : 0,
                OrderCount: c.OrderCount,
                Document: c.Documento);
            mutations.Add(new MutationDto(Guid.NewGuid().ToString(), "web",
                "client.upsert", SyncDtoConverters.Serialize(dto), new DateTimeOffset(c.AlteradoEm).ToUnixTimeMilliseconds()));
        }

        await AppendClientesEspelhadosAsync(mutations, sinceDate, empresaId, lojaId);

        // Pedidos web sem MobileOrderId (criados via /api/pedidos).
        var pedidosQ = _db.Set<Pedido>().IgnoreQueryFilters().AsNoTracking()
            .Include(p => p.Itens)
            .Include(p => p.Pagamentos)
            .Where(p => p.EmpresaId == empresaId && p.AlteradoEm > sinceDate
                && (p.MobileOrderId == null || p.MobileOrderId == ""));
        if (lojaId.HasValue) pedidosQ = pedidosQ.Where(p => p.LojaId == lojaId || p.LojaId == null);
        // #2813 (B5): na primeira carga o cancelado não desce. O entregue só desce dentro da janela, contada da
        // entrega (EntreguEm, ou CriadoEm quando nula), ou enquanto tiver saldo a receber, de qualquer idade: dinheiro
        // a receber não some da tela. O pedido em aberto (aguardando, preparando, pronto...) desce de qualquer data.
        if (primeiraCarga) pedidosQ = pedidosQ.Where(p => p.Status != StatusPedidoMapper.Cancelado);
        var pedidos = await pedidosQ.ToListAsync();
        foreach (var p in pedidos)
        {
            // Uma vez só: o saldo decide o corte da janela e segue no DTO.
            var aReceber = await AReceberDoPedidoMobile.CalcularAsync(p, _saldos, CancellationToken.None);
            if (primeiraCarga && p.Status == StatusPedidoMapper.Entregue
                && (p.EntreguEm ?? p.CriadoEm) < inicioDaJanela && aReceber <= 0m)
                continue;
            // #1474: a linha de frete não é produto a preparar; o nome segue o da vitrine.
            var items = p.Itens.Where(i => !i.EhLinhaDeFrete).Select(i => new OrderItemDto(
                ProductId: i.ProdutoId?.ToString() ?? "",
                Name: NomeCardapio.Exibicao(i.Nome) ?? "",
                Emoji: i.Emoji,
                Unit: i.Unidade,
                // #2083: 1,5 kg desce 1,5. A escala do numeric(14,3) sai fora para o inteiro
                // continuar saindo como inteiro no JSON (2.000 vira 2).
                Qty: i.Quantidade / 1.000000000000000000000000000000000m,
                UnitPrice: i.PrecoUnitario
            )).ToList();
            var dto = new OrderDto(
                Id: p.Id.ToString(),
                ClientId: p.ClienteId?.ToString(),
                ClientSnapshot: new ClientSnapshotDto(p.ClienteNome ?? "", null),
                Items: items,
                Notes: p.Observacoes,
                Total: p.Total.Valor,
                Status: p.Status ?? "aguardando",
                CreatedAt: new DateTimeOffset(p.CriadoEm).ToUnixTimeMilliseconds(),
                UpdatedAt: new DateTimeOffset(p.AlteradoEm).ToUnixTimeMilliseconds(),
                ScheduledDeliveryAt: p.AgendadoParaEm.HasValue
                    ? new DateTimeOffset(p.AgendadoParaEm.Value).ToUnixTimeMilliseconds()
                    : null,
                AReceber: aReceber);
            mutations.Add(new MutationDto(Guid.NewGuid().ToString(), "web",
                "order.upsert", SyncDtoConverters.Serialize(dto), new DateTimeOffset(p.AlteradoEm).ToUnixTimeMilliseconds()));
        }

        // Lotes web sem MobileBatchId (criados direto no admin/web).
        var lotesQ = _db.Set<Lote>().IgnoreQueryFilters().AsNoTracking()
            .Include(l => l.Itens)
            .Where(l => l.EmpresaId == empresaId && l.AlteradoEm > sinceDate
                && (l.MobileBatchId == null || l.MobileBatchId == ""));
        if (lojaId.HasValue) lotesQ = lotesQ.Where(l => l.LojaId == lojaId || l.LojaId == null);
        // #2813 (B5): na primeira carga o lote finalizado ou expirado (Lote.EstaFinalizado) só desce dentro da
        // janela (DataProducao, a data do CreatedAt do DTO); o lote em produção desce de qualquer data.
        if (primeiraCarga)
            lotesQ = lotesQ.Where(l => (l.Status != "finalizado" && l.Status != "expirado") || l.DataProducao >= inicioDaJanela);
        var lotes = await lotesQ.ToListAsync();
        // #2209: o lote web anterior ao #2199, sem número curto, ganha um aqui.
        var semNumero = lotes.Where(l => l.NumeroCurto is null).Select(l => l.Id).ToList();
        var numerosDados = await _numerosCurtos.DosLotesAsync(empresaId, semNumero, CancellationToken.None);
        foreach (var l in lotes)
        {
            var items = l.Itens.Select(it => new BatchItemDto(
                ProductId: it.ProdutoId?.ToString() ?? "",
                Name: it.Nome,
                Emoji: it.Emoji,
                Unit: it.Unidade,
                Qty: it.Quantidade,
                Photo: it.FotoUrl,
                WeightG: it.PesoG,
                ValidityDays: it.ValidadeDias,
                ExpiresAt: it.ExpiraEm.HasValue ? new DateTimeOffset(it.ExpiraEm.Value).ToUnixTimeMilliseconds() : null
            )).ToList();
            var dto = new BatchDto(
                Id: l.Id.ToString(),
                Code: l.Codigo,
                Items: items,
                BatchPhoto: null,
                CreatedAt: new DateTimeOffset(l.DataProducao).ToUnixTimeMilliseconds(),
                Lote: l.Codigo,
                NumeroCurto: l.NumeroCurto ?? numerosDados.GetValueOrDefault(l.Id));
            mutations.Add(new MutationDto(Guid.NewGuid().ToString(), "web",
                "batch.upsert", SyncDtoConverters.Serialize(dto), new DateTimeOffset(l.AlteradoEm).ToUnixTimeMilliseconds()));
        }

        // MovimentoCaixa web sem Referencia="mobile:..." (criados direto no admin).
        // F7-B: incluir tambem estornados.
        // #2731: o estorno feito no console depois do cursor tambem desce, do lancamento do console
        // (criado antes do cursor) e do lancamento do aparelho ("mobile:<id>"), que desce com o id
        // do aparelho para o PWA remove-lo (sync.js, F7-B). Uma consulta, sem N+1.
        var movimentosQ = _db.Set<MovimentoCaixa>().IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.EmpresaId == empresaId
                && (((m.Referencia == null || !m.Referencia.StartsWith("mobile:")) && m.CriadoEm > sinceDate)
                    || m.EstornadoEm > sinceDate));
        if (lojaId.HasValue) movimentosQ = movimentosQ.Where(m => m.LojaId == lojaId || m.LojaId == null);
        // #2813 (B5): na primeira carga só o lançamento dentro da janela (DataMovimento, a data do CreatedAt do
        // DTO) desce, estornado ou não; o histórico do caixa fica no ERP.
        if (primeiraCarga) movimentosQ = movimentosQ.Where(m => m.DataMovimento >= inicioDaJanela);
        var movimentos = await movimentosQ.ToListAsync();
        foreach (var m in movimentos.Where(m => m.Referencia != null && m.Referencia.StartsWith("mobile:")))
            EstornoDoLancamentoDoAparelho(mutations, m);
        foreach (var m in movimentos.Where(m => m.Referencia == null || !m.Referencia.StartsWith("mobile:")))
        {
            string type = m.Tipo switch
            {
                "entrada" => "income",
                "abertura" => "income",
                "saida" => "expense",
                _ => "expense"
            };
            var dto = new CashEntryDto(
                Id: m.Id.ToString(),
                Type: type,
                Amount: m.Valor,
                Description: m.Descricao ?? "",
                CreatedAt: new DateTimeOffset(m.DataMovimento).ToUnixTimeMilliseconds(),
                Estornado: m.EstornadoEm.HasValue,
                Metodo: m.Metodo);
            mutations.Add(new MutationDto(Guid.NewGuid().ToString(), "web",
                "cashEntry.upsert", SyncDtoConverters.Serialize(dto), UltimaMudanca(m)));
        }

        // F7-C — Fechamentos de caixa do web (alterados depois do `since`).
        var fechamentosQ = _db.Set<FechamentoCaixa>().IgnoreQueryFilters().AsNoTracking()
            .Where(f => f.EmpresaId == empresaId && f.FechadoEm > sinceDate);
        if (lojaId.HasValue) fechamentosQ = fechamentosQ.Where(f => f.LojaId == lojaId || f.LojaId == null);
        // #2813 (B5): na primeira carga só o fechamento dentro da janela (FechadoEm) desce.
        if (primeiraCarga) fechamentosQ = fechamentosQ.Where(f => f.FechadoEm >= inicioDaJanela);
        var fechamentos = await fechamentosQ.ToListAsync();
        foreach (var f in fechamentos)
        {
            var dto = new CashClosingDto(
                Id: f.Id.ToString(),
                DateKey: f.Data.ToString("yyyy-MM-dd"),
                ClosedAt: new DateTimeOffset(f.FechadoEm).ToUnixTimeMilliseconds(),
                ClosedByName: f.FechadoPorNome,
                TotalPagamentosPedidos: f.TotalPagamentosPedidos,
                TotalSaidasExtras: f.TotalSaidasExtras,
                SaldoFinal: f.SaldoFinal,
                Notes: f.Observacoes);
            mutations.Add(new MutationDto(Guid.NewGuid().ToString(), "web",
                "closing.upsert", SyncDtoConverters.Serialize(dto), new DateTimeOffset(f.FechadoEm).ToUnixTimeMilliseconds()));
        }
    }
}

using System.Text.Json;
using EasyStock.Application.Services.Pedidos;
using EasyStock.Application.Services.Storefront;
using EasyStock.Api.Mobile.DTOs;
using EasyStock.Domain.Entities.Mobile;
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
    private async Task AppendClientesEspelhadosAsync(List<MutationDto> mutations, DateTime sinceDate, Guid empresaId)
    {
        var espelhados = await (
            from m in _db.Set<Client>().IgnoreQueryFilters().AsNoTracking()
            join c in _db.Set<Cliente>().IgnoreQueryFilters().AsNoTracking() on m.ErpClienteId equals c.Id
            where m.EmpresaId == empresaId && c.EmpresaId == empresaId && c.Ativo && c.AlteradoEm > sinceDate
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

    public async Task AppendAsync(List<MutationDto> mutations, DateTime sinceDate, Guid empresaId, Guid? lojaId)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

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
        foreach (var p in produtos)
        {
            var dto = new ProductDto(
                Id: p.Id.ToString(),
                Name: p.Nome,
                Emoji: null,
                Category: CategoriaDoPwa(p.Categoria?.Nome),
                Unit: null,
                Price: p.PrecoReferencia?.Valor ?? 0m,
                Stock: 0,
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
        var produtosEspelhados = await _db.Set<Produto>().IgnoreQueryFilters().AsNoTracking()
            .Include(p => p.Categoria)
            .Where(p => p.EmpresaId == empresaId && p.AlteradoEm > sinceDate
                && p.Status == StatusProduto.Ativo
                && mobileLinkedProdutos.Contains(p.Id))
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
                var saldo = itensEstoque.Where(i => i.ProdutoId == p.Id).Sum(i => i.QuantidadeAtual?.Value ?? 0m);
                var dto = new ProductDto(
                    Id: espelho.Id,
                    Name: p.Nome,
                    Emoji: espelho.Emoji,
                    Category: CategoriaDoPwa(p.Categoria?.Nome),
                    Unit: espelho.Unit,
                    Price: p.PrecoReferencia?.Valor ?? 0m,
                    Stock: (int)saldo,
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
                mutations.Add(new MutationDto(Guid.NewGuid().ToString(), "web",
                    "product.upsert", SyncDtoConverters.Serialize(dto), new DateTimeOffset(p.AlteradoEm).ToUnixTimeMilliseconds()));
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

        await AppendClientesEspelhadosAsync(mutations, sinceDate, empresaId);

        // Pedidos web sem MobileOrderId (criados via /api/pedidos).
        var pedidosQ = _db.Set<Pedido>().IgnoreQueryFilters().AsNoTracking()
            .Include(p => p.Itens)
            .Include(p => p.Pagamentos)
            .Where(p => p.EmpresaId == empresaId && p.AlteradoEm > sinceDate
                && (p.MobileOrderId == null || p.MobileOrderId == ""));
        if (lojaId.HasValue) pedidosQ = pedidosQ.Where(p => p.LojaId == lojaId || p.LojaId == null);
        var pedidos = await pedidosQ.ToListAsync();
        foreach (var p in pedidos)
        {
            // #1474: a linha de frete não é produto a preparar; o nome segue o da vitrine.
            var items = p.Itens.Where(i => !i.EhLinhaDeFrete).Select(i => new OrderItemDto(
                ProductId: i.ProdutoId?.ToString() ?? "",
                Name: NomeCardapio.Exibicao(i.Nome) ?? "",
                Emoji: i.Emoji,
                Unit: i.Unidade,
                Qty: (int)i.Quantidade,
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
                AReceber: await AReceberDoPedidoMobile.CalcularAsync(p, _saldos, CancellationToken.None));
            mutations.Add(new MutationDto(Guid.NewGuid().ToString(), "web",
                "order.upsert", SyncDtoConverters.Serialize(dto), new DateTimeOffset(p.AlteradoEm).ToUnixTimeMilliseconds()));
        }

        // Lotes web sem MobileBatchId (criados direto no admin/web).
        var lotesQ = _db.Set<Lote>().IgnoreQueryFilters().AsNoTracking()
            .Include(l => l.Itens)
            .Where(l => l.EmpresaId == empresaId && l.AlteradoEm > sinceDate
                && (l.MobileBatchId == null || l.MobileBatchId == ""));
        if (lojaId.HasValue) lotesQ = lotesQ.Where(l => l.LojaId == lojaId || l.LojaId == null);
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
        var movimentosQ = _db.Set<MovimentoCaixa>().IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.EmpresaId == empresaId && m.CriadoEm > sinceDate
                && (m.Referencia == null || !m.Referencia.StartsWith("mobile:")));
        if (lojaId.HasValue) movimentosQ = movimentosQ.Where(m => m.LojaId == lojaId || m.LojaId == null);
        var movimentos = await movimentosQ.ToListAsync();
        foreach (var m in movimentos)
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
                "cashEntry.upsert", SyncDtoConverters.Serialize(dto), new DateTimeOffset(m.CriadoEm).ToUnixTimeMilliseconds()));
        }

        // F7-C — Fechamentos de caixa do web (alterados depois do `since`).
        var fechamentosQ = _db.Set<FechamentoCaixa>().IgnoreQueryFilters().AsNoTracking()
            .Where(f => f.EmpresaId == empresaId && f.FechadoEm > sinceDate);
        if (lojaId.HasValue) fechamentosQ = fechamentosQ.Where(f => f.LojaId == lojaId || f.LojaId == null);
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

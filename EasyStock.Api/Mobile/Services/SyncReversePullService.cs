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
    ILogger<SyncReversePullService> log)
{
    private readonly EasyStockDbContext _db = db;
    private readonly IConfiguration _config = config;
    private readonly ILogger<SyncReversePullService> _log = log;

    public async Task AppendAsync(List<MutationDto> mutations, DateTime sinceDate, Guid empresaId, Guid? lojaId)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Produtos web sem mobile_product correspondente (Erp link).
        var mobileLinkedProdutos = await _db.Set<Product>().IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.EmpresaId == empresaId && p.ErpProductId != null)
            .Select(p => p.ErpProductId!.Value).ToListAsync();
        var produtosQ = _db.Set<Produto>().IgnoreQueryFilters().AsNoTracking()
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
                Category: "Geral",
                Unit: null,
                Price: p.PrecoReferencia?.Valor ?? 0m,
                Stock: 0,
                Custom: false,
                Sku: p.CodigoBarras,
                DefaultWeightG: null,
                DefaultValidityDays: null);
            mutations.Add(new MutationDto(Guid.NewGuid().ToString(), "web",
                "product.upsert", SyncDtoConverters.Serialize(dto), new DateTimeOffset(p.AlteradoEm).ToUnixTimeMilliseconds()));
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
                OrderCount: c.OrderCount);
            mutations.Add(new MutationDto(Guid.NewGuid().ToString(), "web",
                "client.upsert", SyncDtoConverters.Serialize(dto), new DateTimeOffset(c.AlteradoEm).ToUnixTimeMilliseconds()));
        }

        // Pedidos web sem MobileOrderId (criados via /api/pedidos).
        var pedidosQ = _db.Set<Pedido>().IgnoreQueryFilters().AsNoTracking()
            .Include(p => p.Itens)
            .Where(p => p.EmpresaId == empresaId && p.AlteradoEm > sinceDate
                && (p.MobileOrderId == null || p.MobileOrderId == ""));
        if (lojaId.HasValue) pedidosQ = pedidosQ.Where(p => p.LojaId == lojaId || p.LojaId == null);
        var pedidos = await pedidosQ.ToListAsync();
        foreach (var p in pedidos)
        {
            var items = p.Itens.Select(i => new OrderItemDto(
                ProductId: i.ProdutoId?.ToString() ?? "",
                Name: i.Nome ?? "",
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
                    : null);
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
                Lote: l.Codigo);
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

using System.Security.Claims;
using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Infra.Postgre.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.Mobile.Controllers;

/// <summary>
/// Onda 2 — Catálogo unificado mobile ↔ ERP.
///
/// Operadores web usam estes endpoints pra revisar produtos custom criados
/// no app (<c>IsCustom=true</c>, <c>IsApproved=false</c>, <c>ErpProductId=null</c>),
/// linkar a um <c>Produto</c> ERP existente OU rejeitar.
///
/// Auditoria 2026-04-30: tenant guard via <see cref="MobileManagementControllerBase"/>.
/// </summary>
[ApiController]
[Route("api/mobile/products")]
[Authorize]
public class MobileProductsController(
    EasyStockDbContext db,
    ICurrentUserAccessor currentUser,
    ILogger<MobileProductsController> log) : MobileManagementControllerBase(currentUser)
{
    private readonly EasyStockDbContext _db = db;
    private readonly ILogger<MobileProductsController> _log = log;

    /// <summary>
    /// Lista produtos da empresa filtrável por status. Usado pelo painel
    /// /produtos-mobile pra mostrar "pendentes de aprovação".
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] Guid? empresaId,
        [FromQuery] bool? pendingOnly,
        [FromQuery] bool? customOnly,
        CancellationToken ct)
    {
        if (!TryResolveEmpresaId(empresaId, out var emp, out var err)) return err!;

        var q = _db.Set<Product>().AsNoTracking().Where(p => p.EmpresaId == emp);
        if (pendingOnly == true) q = q.Where(p => p.IsCustom && !p.IsApproved && p.ErpProductId == null);
        if (customOnly == true) q = q.Where(p => p.IsCustom);

        var items = await q.OrderByDescending(p => p.CreatedAt).Take(200).ToListAsync(ct);

        return Ok(items.Select(p => new MobileProductSummary(
            Id: p.Id,
            Name: p.Name,
            Emoji: p.Emoji,
            Category: p.Category,
            Unit: p.Unit,
            Price: p.Price,
            Stock: p.Stock,
            IsCustom: p.IsCustom,
            IsApproved: p.IsApproved,
            ErpProductId: p.ErpProductId,
            EmpresaId: p.EmpresaId,
            LojaId: p.LojaId,
            CreatedAt: p.CreatedAt,
            UpdatedAt: p.UpdatedAt,
            ApprovedAt: p.ApprovedAt,
            LastDeviceId: p.LastDeviceId,
            LastOperatorName: p.LastOperatorName
        )).ToArray());
    }

    /// <summary>
    /// Aprova um produto custom sem linkar a ERP — só marca como revisado.
    /// Útil quando o produto é mobile-only (ex: serviço pontual) e não
    /// precisa entrar no catálogo principal.
    /// </summary>
    [HttpPost("{id}/approve")]
    public async Task<IActionResult> Approve(string id, [FromQuery] Guid? empresaId, CancellationToken ct)
    {
        if (!TryResolveEmpresaId(empresaId, out var emp, out var err)) return err!;
        var product = await _db.Set<Product>().FirstOrDefaultAsync(p => p.Id == id && p.EmpresaId == emp, ct);
        if (product == null) return NotFound();

        product.IsApproved = true;
        product.ApprovedAt = DateTime.UtcNow;
        product.ApprovedByUserId = ResolveUserId();
        product.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("Mobile product aprovado (sem link ERP): {ProductId} by {UserId}",
            id, product.ApprovedByUserId);
        return NoContent();
    }

    /// <summary>
    /// Linka um <c>mobile_product</c> a um <c>Produto</c> ERP existente.
    /// Marca como aprovado. Próxima sync do mobile recebe atualizações
    /// do produto ERP (nome, preço) e — quando reconciliação de stock
    /// for ativada — também valor de estoque.
    /// </summary>
    [HttpPost("{id}/link")]
    public async Task<IActionResult> Link(string id, [FromBody] LinkRequest req, [FromQuery] Guid? empresaId, CancellationToken ct)
    {
        if (req == null || req.ErpProductId == Guid.Empty)
            return BadRequest(new { error = "erpProductId obrigatório" });
        if (!TryResolveEmpresaId(empresaId, out var emp, out var err)) return err!;

        var product = await _db.Set<Product>().FirstOrDefaultAsync(p => p.Id == id && p.EmpresaId == emp, ct);
        if (product == null) return NotFound();

        // Verifica que o Produto ERP existe e pertence à mesma empresa.
        // Sem tipar a entidade ERP aqui pra não criar dependência circular —
        // queries via EF Set<T>() exigiriam o type. Usamos SQL parametrizado.
        var produtoExists = await _db.Database
            .SqlQueryRaw<int>(@"
                SELECT 1 FROM produtos
                WHERE ""Id"" = {0} AND ""EmpresaId"" = {1}
                LIMIT 1", req.ErpProductId, product.EmpresaId ?? Guid.Empty)
            .ToListAsync(ct);

        if (produtoExists.Count == 0)
        {
            return BadRequest(new { error = "Produto ERP não encontrado nesta empresa" });
        }

        product.ErpProductId = req.ErpProductId;
        product.IsApproved = true;
        product.ApprovedAt = DateTime.UtcNow;
        product.ApprovedByUserId = ResolveUserId();
        product.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("Mobile product {MobileId} linkado a Produto ERP {ErpId} by {UserId}",
            id, req.ErpProductId, product.ApprovedByUserId);
        return NoContent();
    }

    /// <summary>
    /// Lista divergências entre <c>mobile_products.Stock</c> e
    /// <c>itens_estoque.QuantidadeAtual</c> pra produtos linkados ao ERP.
    /// Painel /produtos-mobile usa pra mostrar aba "Divergências".
    /// </summary>
    [HttpGet("/api/mobile/stock/divergences")]
    public async Task<IActionResult> ListDivergences(
        [FromQuery] Guid? empresaId,
        CancellationToken ct)
    {
        if (!TryResolveEmpresaId(empresaId, out var emp, out var err)) return err!;

        // Produtos mobile linkados ao ERP — único caso onde reconciliação faz sentido.
        var mobileLinked = await _db.Set<Product>().AsNoTracking()
            .Where(p => p.EmpresaId == emp && p.ErpProductId != null)
            .ToListAsync(ct);

        if (mobileLinked.Count == 0) return Ok(Array.Empty<StockDivergence>());

        var produtoIds = mobileLinked.Select(p => p.ErpProductId!.Value).Distinct().ToList();
        var itensEstoque = await _db.Set<ItemEstoque>().AsNoTracking()
            .Where(i => i.EmpresaId == emp && produtoIds.Contains(i.ProdutoId))
            .ToListAsync(ct);

        // Match (ProdutoId, LojaId) — se LojaId é null no ItemEstoque, casa
        // com qualquer mobile. Senão exige loja igual.
        var divergences = new List<StockDivergence>();
        foreach (var mp in mobileLinked)
        {
            var item = itensEstoque.FirstOrDefault(i =>
                i.ProdutoId == mp.ErpProductId &&
                (i.LojaId == null || i.LojaId == mp.LojaId));
            var erpStock = item?.QuantidadeAtual?.Value ?? 0;
            if (mp.Stock == erpStock) continue; // bate, sem divergência
            divergences.Add(new StockDivergence(
                MobileProductId: mp.Id,
                ProductName: mp.Name,
                Emoji: mp.Emoji,
                ErpProductId: mp.ErpProductId!.Value,
                LojaId: mp.LojaId,
                MobileStock: mp.Stock,
                ErpStock: erpStock,
                Delta: erpStock - mp.Stock,
                ItemEstoqueExists: item != null,
                LastDeviceId: mp.LastDeviceId,
                LastOperatorName: mp.LastOperatorName,
                UpdatedAt: mp.UpdatedAt
            ));
        }
        return Ok(divergences.OrderByDescending(d => Math.Abs(d.Delta)).ToArray());
    }

    /// <summary>
    /// Reconcilia 1 produto: força o stock do mobile pra bater com o ERP.
    /// Use quando o operador confirma que o ERP está certo (ex: contagem física).
    /// </summary>
    [HttpPost("{id}/reconcile-stock")]
    public async Task<IActionResult> ReconcileStock(string id, [FromQuery] Guid? empresaId, CancellationToken ct)
    {
        if (!TryResolveEmpresaId(empresaId, out var emp, out var err)) return err!;
        var product = await _db.Set<Product>().FirstOrDefaultAsync(p => p.Id == id && p.EmpresaId == emp, ct);
        if (product == null) return NotFound();
        if (product.ErpProductId == null)
            return BadRequest(new { error = "Produto não está linkado ao ERP" });

        var item = await _db.Set<ItemEstoque>().AsNoTracking().FirstOrDefaultAsync(i =>
            i.EmpresaId == product.EmpresaId &&
            i.ProdutoId == product.ErpProductId &&
            (i.LojaId == null || i.LojaId == product.LojaId), ct);

        if (item == null)
            return BadRequest(new { error = "ItemEstoque não existe no ERP. Crie o estoque inicial primeiro." });

        var oldStock = product.Stock;
        product.Stock = item.QuantidadeAtual?.Value ?? 0;
        product.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _log.LogInformation(
            "Reconcile stock: mobile_product {Id} {Old}→{New} (ERP item={ItemId})",
            id, oldStock, product.Stock, item.Id);

        return Ok(new { mobileStock = product.Stock, previousStock = oldStock });
    }

    /// <summary>
    /// Desfaz a aprovação/link. Útil em caso de erro de operador.
    /// </summary>
    [HttpPost("{id}/unlink")]
    public async Task<IActionResult> Unlink(string id, [FromQuery] Guid? empresaId, CancellationToken ct)
    {
        if (!TryResolveEmpresaId(empresaId, out var emp, out var err)) return err!;
        var product = await _db.Set<Product>().FirstOrDefaultAsync(p => p.Id == id && p.EmpresaId == emp, ct);
        if (product == null) return NotFound();

        product.ErpProductId = null;
        product.IsApproved = false;
        product.ApprovedAt = null;
        product.ApprovedByUserId = null;
        product.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("Mobile product {ProductId} unlink/un-approve", id);
        return NoContent();
    }

    private Guid? ResolveUserId()
    {
        var sub = User.FindFirstValue("sub")
                  ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(sub, out var g) ? g : null;
    }
}

// ---------- DTOs ----------

public record MobileProductSummary(
    string Id,
    string Name,
    string? Emoji,
    string Category,
    string? Unit,
    decimal? Price,
    int Stock,
    bool IsCustom,
    bool IsApproved,
    Guid? ErpProductId,
    Guid? EmpresaId,
    Guid? LojaId,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? ApprovedAt,
    string? LastDeviceId,
    string? LastOperatorName
);

public record LinkRequest(Guid ErpProductId);

/// <summary>
/// Item retornado pela aba "Divergências" do painel /produtos-mobile.
/// </summary>
public record StockDivergence(
    string MobileProductId,
    string ProductName,
    string? Emoji,
    Guid ErpProductId,
    Guid? LojaId,
    int MobileStock,
    int ErpStock,
    int Delta,
    bool ItemEstoqueExists,
    string? LastDeviceId,
    string? LastOperatorName,
    DateTime UpdatedAt
);

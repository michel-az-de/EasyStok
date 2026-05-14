using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.Controllers;

/// <summary>
/// F10-D — Consulta de auditoria universal (entity_alteracoes).
/// Permite buscar timeline de alteracoes por entidade, tipo ou empresa.
/// Autenticacao: JWT (web/api). SuperAdmin pode consultar cross-tenant.
/// </summary>
[ApiController]
[Route("api/audit")]
[Authorize]
public class EntityAuditController(
    EasyStockDbContext db,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    /// <summary>
    /// Timeline de uma entidade especifica.
    /// GET /api/audit/entity/{tipoEntidade}/{entidadeId}?page=1&amp;pageSize=50
    /// </summary>
    [HttpGet("entity/{tipoEntidade}/{entidadeId:guid}")]
    public async Task<IActionResult> GetByEntity(
        string tipoEntidade,
        Guid entidadeId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var empresaId = currentUser.EmpresaId;
        if (empresaId == Guid.Empty)
            return Unauthorized();

        pageSize = Math.Clamp(pageSize, 1, 100);
        page = Math.Max(1, page);

        var query = db.EntityAlteracoes.AsNoTracking()
            .Where(a => a.EmpresaId == empresaId
                     && a.TipoEntidade == tipoEntidade
                     && a.EntidadeId == entidadeId)
            .OrderByDescending(a => a.AlteradoEm);

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new
            {
                a.Id,
                a.Acao,
                a.Campo,
                a.ValorAntigo,
                a.ValorNovo,
                a.AlteradoPorNome,
                a.Origem,
                a.AlteradoEm,
                hasPii = a.PiiCriptografado != null
            })
            .ToListAsync(ct);

        return DataOk(new { total, page, pageSize, items });
    }

    /// <summary>
    /// Timeline cross-entidade para um cliente (pedidos, vendas, pagamentos, etc).
    /// GET /api/audit/client-timeline/{clienteId}?page=1&amp;pageSize=50
    /// </summary>
    [HttpGet("client-timeline/{clienteId:guid}")]
    public async Task<IActionResult> GetClientTimeline(
        Guid clienteId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var empresaId = currentUser.EmpresaId;
        if (empresaId == Guid.Empty)
            return Unauthorized();

        pageSize = Math.Clamp(pageSize, 1, 100);
        page = Math.Max(1, page);

        // Busca alteracoes do cliente + alteracoes de pedidos desse cliente
        var clienteEntries = db.EntityAlteracoes.AsNoTracking()
            .Where(a => a.EmpresaId == empresaId
                     && a.TipoEntidade == "Cliente"
                     && a.EntidadeId == clienteId);

        // Pedidos do cliente (buscamos os IDs primeiro)
        var pedidoIds = await db.Pedidos.AsNoTracking()
            .Where(p => p.EmpresaId == empresaId && p.ClienteId == clienteId)
            .Select(p => p.Id)
            .ToListAsync(ct);

        var pedidoEntries = db.EntityAlteracoes.AsNoTracking()
            .Where(a => a.EmpresaId == empresaId
                     && (a.TipoEntidade == "Pedido" || a.TipoEntidade == "PedidoItem" || a.TipoEntidade == "PedidoPagamento")
                     && pedidoIds.Contains(a.EntidadeId));

        var union = clienteEntries.Union(pedidoEntries)
            .OrderByDescending(a => a.AlteradoEm);

        var total = await union.CountAsync(ct);
        var items = await union
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new
            {
                a.Id,
                a.TipoEntidade,
                a.EntidadeId,
                a.Acao,
                a.Campo,
                a.ValorAntigo,
                a.ValorNovo,
                a.AlteradoPorNome,
                a.Origem,
                a.AlteradoEm
            })
            .ToListAsync(ct);

        return DataOk(new { total, page, pageSize, items });
    }

    /// <summary>
    /// Resumo de auditoria da empresa — contagem por tipo de entidade.
    /// GET /api/audit/summary
    /// </summary>
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(CancellationToken ct = default)
    {
        var empresaId = currentUser.EmpresaId;
        if (empresaId == Guid.Empty)
            return Unauthorized();

        var summary = await db.EntityAlteracoes.AsNoTracking()
            .Where(a => a.EmpresaId == empresaId)
            .GroupBy(a => a.TipoEntidade)
            .Select(g => new { tipo = g.Key, count = g.Count() })
            .ToListAsync(ct);

        var total = summary.Sum(s => s.count);

        return DataOk(new { total, byType = summary });
    }
}

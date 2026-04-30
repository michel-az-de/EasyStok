using EasyStock.Api.Http;
using EasyStock.Api.Services;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.Controllers;

[ApiController]
[Route("api/admin/cupons")]
[Authorize(Policy = "SuperAdmin")]
public class AdminCuponsController(EasyStockDbContext db, AdminAuditService audit) : EasyStockControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetCupons()
    {
        var cupons = await db.Cupons
            .OrderByDescending(c => c.CriadoEm)
            .Select(c => new
            {
                c.Id,
                c.Codigo,
                tipoDesconto = c.TipoDesconto.ToString(),
                c.Valor,
                c.LimiteUsos,
                c.TotalUsos,
                c.ValidoAte,
                c.PlanoId,
                c.Ativo,
                c.CriadoEm
            })
            .ToListAsync();

        return DataOk(cupons);
    }

    [HttpPost]
    public async Task<IActionResult> CreateCupom([FromBody] CreateCupomRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Codigo))
            return DataBadRequest("Código é obrigatório.");

        if (!Enum.TryParse<TipoDesconto>(req.TipoDesconto, out var tipo))
            return DataBadRequest("TipoDesconto inválido. Valores: Percentual, ValorFixo, MesesGratis");

        var codigo = req.Codigo.ToUpperInvariant();
        if (await db.Cupons.AnyAsync(c => c.Codigo == codigo))
            return Conflict(new { error = new { code = "CODIGO_DUPLICADO", message = "Já existe um cupom com este código." } });

        var cupom = Cupom.Criar(codigo, tipo, req.Valor, req.LimiteUsos, req.ValidoAte, req.PlanoId);
        db.Cupons.Add(cupom);
        await db.CommitAsync();
        await audit.LogAsync("CupomCriado", $"Codigo={cupom.Codigo}");

        return DataCreated($"/api/admin/cupons/{cupom.Id}", new { cupom.Id, cupom.Codigo });
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> PatchCupom(Guid id, [FromBody] PatchCupomRequest req)
    {
        var cupom = await db.Cupons.FindAsync(id);
        if (cupom is null) return DataNotFound("Cupom não encontrado.");

        TipoDesconto? tipo = null;
        if (!string.IsNullOrWhiteSpace(req.TipoDesconto))
        {
            if (!Enum.TryParse<TipoDesconto>(req.TipoDesconto, out var t))
                return DataBadRequest("TipoDesconto inválido.");
            tipo = t;
        }

        cupom.Atualizar(req.Codigo, tipo, req.Valor, req.LimiteUsos, req.ValidoAte, req.PlanoId);
        await db.CommitAsync();
        await audit.LogAsync("CupomAtualizado", $"CupomId={id}");

        return DataOk(new { cupom.Id, cupom.Codigo });
    }

    [HttpPatch("{id:guid}/toggle")]
    public async Task<IActionResult> ToggleCupom(Guid id)
    {
        var cupom = await db.Cupons.FindAsync(id);
        if (cupom is null) return DataNotFound("Cupom não encontrado.");

        cupom.Toggle();
        await db.CommitAsync();
        await audit.LogAsync("CupomToggle", $"CupomId={id}, Ativo={cupom.Ativo}");

        return DataOk(new { cupom.Id, cupom.Ativo });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteCupom(Guid id)
    {
        var cupom = await db.Cupons.FindAsync(id);
        if (cupom is null) return DataNotFound("Cupom não encontrado.");

        if (cupom.TotalUsos > 0)
            return Conflict(new { error = new { code = "CUPOM_EM_USO", message = "Não é possível excluir um cupom que já foi utilizado." } });

        db.Cupons.Remove(cupom);
        await db.CommitAsync();
        await audit.LogAsync("CupomExcluido", $"Codigo={cupom.Codigo}");

        return DataOk(new { id });
    }
}

public record CreateCupomRequest(
    string Codigo,
    string TipoDesconto,
    decimal Valor,
    int? LimiteUsos,
    DateTime? ValidoAte,
    Guid? PlanoId);

public record PatchCupomRequest(
    string? Codigo,
    string? TipoDesconto,
    decimal? Valor,
    int? LimiteUsos,
    DateTime? ValidoAte,
    Guid? PlanoId);

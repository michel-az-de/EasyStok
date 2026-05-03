using EasyStock.Api.Http;
using EasyStock.Api.Services;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Constants;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace EasyStock.Api.Controllers;

[ApiController]
[Route("api/admin/tenants")]
[Authorize(Policy = "SuperAdmin")]
public class AdminTenantsController(
    EasyStockDbContext db,
    IAdminTenantsQueries tenantsQueries,
    ICurrentUserAccessor currentUser,
    IConfiguration configuration,
    AdminAuditService audit) : EasyStockControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetTenants(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] string? status = null)
    {
        (page, pageSize) = NormalisePage(page, pageSize);

        StatusAssinatura? filtroStatus = null;
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<StatusAssinatura>(status, out var se))
            filtroStatus = se;

        var (items, total) = await tenantsQueries.ListarAsync(page, pageSize, search, filtroStatus);
        return DataPaged(items, total, page, pageSize);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetTenant(Guid id)
    {
        var detalhe = await tenantsQueries.ObterDetalheAsync(id);
        if (detalhe is null) return DataNotFound("Tenant não encontrado.");
        return DataOk(detalhe);
    }

    // TODO B4 follow-up: extrair os 6 endpoints de mutação abaixo para UseCases dedicados
    // (PatchStatus, Impersonate, PatchPlano, GetAudit, GrantTrial, AplicarCupom). Hoje
    // ainda dependem de db.AssinaturasEmpresa / db.AdminImpersonationLogs / db.Cupons /
    // db.AuditLogs direto — violação leve, gated por SuperAdmin policy. As leituras
    // complexas (GetTenants/GetTenant) já foram migradas para IAdminTenantsQueries.

    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> PatchStatus(Guid id, [FromBody] PatchTenantStatusRequest req)
    {
        var assinatura = await db.AssinaturasEmpresa
            .Where(a => a.EmpresaId == id)
            .OrderByDescending(a => a.DataInicio)
            .FirstOrDefaultAsync();

        if (assinatura is null) return DataNotFound("Assinatura não encontrada.");

        if (!Enum.TryParse<StatusAssinatura>(req.Status, out var novoStatus))
            return DataBadRequest("Status inválido.", "Valores aceitos: Ativa, Suspensa, Cancelada");

        switch (novoStatus)
        {
            case StatusAssinatura.Suspensa: assinatura.Suspender(); break;
            case StatusAssinatura.Cancelada: assinatura.Cancelar(); break;
            case StatusAssinatura.Ativa: assinatura.Reativar(); break;
            default: return DataBadRequest("Status não suportado.");
        }

        db.AuditLogs.Add(AuditLog.Criar(
            currentUser.UsuarioId,
            $"AdminAlterarStatusTenant:{novoStatus}",
            true,
            $"EmpresaId={id}. Motivo: {req.Motivo}",
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            null));

        await db.CommitAsync();
        await audit.LogAsync("TenantStatusAlterado", $"Status={novoStatus}, Motivo={req.Motivo}", id);
        return DataOk(new { status = novoStatus.ToString() });
    }

    [HttpPost("{id:guid}/impersonate")]
    public async Task<IActionResult> Impersonate(Guid id)
    {
        var empresa = await db.Empresas.FindAsync(id);
        if (empresa is null) return DataNotFound("Tenant não encontrado.");

        // Busca o usuário com perfil Admin na empresa (ou o primeiro usuário ativo)
        var ue = await db.UsuariosEmpresas
            .Include(x => x.Usuario)
            .Where(x => x.EmpresaId == id && x.Usuario!.Ativo)
            .OrderBy(x => x.CriadoEm)
            .FirstOrDefaultAsync();

        if (ue?.Usuario is null)
            return DataNotFound("Nenhum usuário ativo encontrado nesta empresa.");

        // Busca o perfil/nivel do usuário nesta empresa
        var perfil = await db.UsuariosPerfis
            .Include(up => up.Perfil)
            .Where(up => up.UsuarioId == ue.UsuarioId && up.EmpresaId == id)
            .FirstOrDefaultAsync();

        // Cap at Admin — impersonation must never produce a SuperAdmin token
        var rawNivel = perfil?.Perfil?.Nivel ?? NivelAcesso.Admin;
        var nivel = rawNivel == NivelAcesso.SuperAdmin ? NivelAcesso.Admin : rawNivel;

        var secretKey = configuration["Jwt:SecretKey"];
        if (string.IsNullOrEmpty(secretKey))
            return DataBadRequest("Configuração JWT ausente.", "Jwt:SecretKey não configurado.");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new("sub", ue.Usuario.Id.ToString()),
            new("email", ue.Usuario.Email),
            new("nome", ue.Usuario.Nome),
            new("nivel", nivel.ToString()),
            new("empresaId", id.ToString()),
            new("impersonated_by", currentUser.UsuarioId.ToString())
        };

        var agora = DateTime.UtcNow;
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            // NotBefore + IssuedAt explicitos para que validacao do JWT rejeite tokens com
            // timestamps inconsistentes ou clones gerados fora desta sessao.
            NotBefore = agora,
            IssuedAt = agora,
            Expires = agora.AddSeconds(900),
            Issuer = configuration["Jwt:Issuer"],
            Audience = configuration["Jwt:Audience"],
            SigningCredentials = credentials
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.WriteToken(tokenHandler.CreateToken(tokenDescriptor));

        db.AdminImpersonationLogs.Add(AdminImpersonationLog.Criar(
            currentUser.UsuarioId,
            id,
            HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""));

        await db.CommitAsync();
        await audit.LogAsync("TenantImpersonado", $"EmpresaId={id}", id);
        return DataOk(new { token, expiresIn = 900 });
    }

    [HttpPatch("{id:guid}/plano")]
    public async Task<IActionResult> PatchPlano(Guid id, [FromBody] PatchTenantPlanoRequest req)
    {
        var assinatura = await db.AssinaturasEmpresa
            .Where(a => a.EmpresaId == id && a.Status == StatusAssinatura.Ativa)
            .OrderByDescending(a => a.DataInicio)
            .FirstOrDefaultAsync();

        if (assinatura is null) return DataNotFound("Assinatura ativa não encontrada.");

        var plano = await db.Planos.FindAsync(req.PlanoId);
        if (plano is null) return DataNotFound("Plano não encontrado.");

        assinatura.PlanoId = req.PlanoId;
        assinatura.AlteradoEm = DateTime.UtcNow;
        await db.CommitAsync();
        await audit.LogAsync("TenantPlanoAlterado", $"PlanoId={req.PlanoId}, PlanoNome={plano.Nome}", id);

        return DataOk(new { planoId = req.PlanoId, planoNome = plano.Nome });
    }

    [HttpGet("{id:guid}/audit")]
    public async Task<IActionResult> GetAudit(Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        (page, pageSize) = NormalisePage(page, pageSize);

        var usuariosIds = await db.UsuariosEmpresas
            .Where(ue => ue.EmpresaId == id)
            .Select(ue => ue.UsuarioId)
            .ToListAsync();

        var query = db.AuditLogs.Where(a => usuariosIds.Contains(a.UsuarioId));
        var total = await query.CountAsync();

        var logs = await query
            .OrderByDescending(a => a.DataHora)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new { a.Id, a.UsuarioId, a.Acao, a.Sucesso, a.Detalhes, a.Ip, a.DataHora })
            .ToListAsync();

        return DataPaged(logs, total, page, pageSize);
    }

    [HttpPost("{id:guid}/trial")]
    public async Task<IActionResult> GrantTrial(Guid id, [FromBody] GrantTrialRequest req)
    {
        if (req.DiasTrial < 1 || req.DiasTrial > 90)
            return DataBadRequest("DiasTrial deve estar entre 1 e 90.");

        var assinatura = await db.AssinaturasEmpresa
            .Where(a => a.EmpresaId == id)
            .OrderByDescending(a => a.DataInicio)
            .FirstOrDefaultAsync();

        if (assinatura is null) return DataNotFound("Assinatura não encontrada.");

        assinatura.AtivarTrial(req.DiasTrial);
        await db.CommitAsync();
        await audit.LogAsync("TrialConcedido", $"Dias={req.DiasTrial}, TrialFim={assinatura.TrialFim:O}", id);

        return DataOk(new { trialFim = assinatura.TrialFim });
    }

    [HttpPost("{id:guid}/aplicar-cupom")]
    public async Task<IActionResult> AplicarCupom(Guid id, [FromBody] AplicarCupomRequest req)
    {
        var assinatura = await db.AssinaturasEmpresa
            .Where(a => a.EmpresaId == id)
            .OrderByDescending(a => a.DataInicio)
            .FirstOrDefaultAsync();

        if (assinatura is null) return DataNotFound("Assinatura não encontrada.");

        var cupom = await db.Cupons.FirstOrDefaultAsync(c => c.Codigo == req.Codigo.ToUpperInvariant());
        if (cupom is null) return DataNotFound("Cupom não encontrado.");

        if (!cupom.PodeUsarEm(DateTime.UtcNow))
            return Conflict(new { error = new { code = "CUPOM_INVALIDO", message = "Cupom inválido, expirado ou esgotado." } });

        assinatura.AplicarCupom(cupom);
        await db.CommitAsync();
        await audit.LogAsync("CupomAplicado", $"Codigo={cupom.Codigo}, Desconto={cupom.Valor}", id);

        return DataOk(new { cupomCodigo = cupom.Codigo, descontoAplicado = cupom.Valor });
    }
}

public record PatchTenantStatusRequest(string Status, string? Motivo);
public record PatchTenantPlanoRequest(Guid PlanoId);
public record GrantTrialRequest(int DiasTrial);
public record AplicarCupomRequest(string Codigo);

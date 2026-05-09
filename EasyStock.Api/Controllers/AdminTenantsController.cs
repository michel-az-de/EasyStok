using EasyStock.Api.Http;
using EasyStock.Api.Services;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Admin.CriarTenantPorAdmin;
using EasyStock.Application.UseCases.Common;
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
    IAssinaturaEmpresaRepository assinaturaRepo,
    IPlanoRepository planoRepo,
    ICupomRepository cupomRepo,
    IAuditLogRepository auditLogRepo,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser,
    IConfiguration configuration,
    AdminAuditService audit,
    CriarTenantPorAdminUseCase criarTenantUseCase,
    ILogger<AdminTenantsController> logger) : EasyStockControllerBase
{
    private const int MotivoMinimo = 10;
    // ─────────────────── Cadastro manual de tenant pelo back-office ───────────────────

    /// <summary>
    /// Cadastra um cliente (tenant) manualmente pelo operador SuperAdmin. Use case típico:
    /// cliente acionou suporte sem conta, ou admin original saiu da empresa e precisamos
    /// recriar acesso. Cria empresa + usuário admin inicial (Starter + trial 14d) com
    /// senha temporária retornada 1x — o operador exibe pro cliente e/ou envia por email.
    /// Justificativa (≥10 chars) obrigatória → AdminAuditLog.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CriarManual([FromBody] CriarTenantManualRequest req)
    {
        if (!ValidarMotivo(req?.Motivo, out var motivo, out var erro)) return DataBadRequest(erro!);

        try
        {
            var resultado = await criarTenantUseCase.ExecuteAsync(new CriarTenantPorAdminCommand(
                NomeEmpresa: req!.NomeEmpresa ?? string.Empty,
                Documento: req.Documento,
                NomeAdmin: req.NomeAdmin ?? string.Empty,
                EmailAdmin: req.EmailAdmin ?? string.Empty,
                EnviarEmail: req.EnviarEmail ?? true));

            await audit.LogAsync(
                "AdminCriouTenantManual",
                $"EmpresaId={resultado.TenantId}, Nome={resultado.NomeEmpresa}, AdminEmail={MascararEmail(resultado.EmailAdmin)}, EmailEnviado={resultado.EmailEnviado}",
                tenantId: resultado.TenantId,
                motivo: motivo,
                entidadeAfetadaId: resultado.TenantId);

            // Senha temporária no payload — UI deve exibir 1x e pedir pro operador anotar.
            // Não logar em loggers — vaza em arquivos. Audit log já omite (só guarda metadados).
            return DataCreated($"/api/admin/tenants/{resultado.TenantId}", new
            {
                tenantId = resultado.TenantId,
                usuarioId = resultado.UsuarioId,
                nomeEmpresa = resultado.NomeEmpresa,
                nomeAdmin = resultado.NomeAdmin,
                emailAdmin = resultado.EmailAdmin,
                senhaTemporaria = resultado.SenhaTemporaria,
                emailEnviado = resultado.EmailEnviado,
                emailErro = resultado.EmailErro,
                trialFim = resultado.TrialFim
            });
        }
        catch (UseCaseValidationException ex) { return DataBadRequest(ex.Message); }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao cadastrar tenant manualmente");
            return Problem(detail: ex.Message, statusCode: 500, title: "Erro ao cadastrar cliente.");
        }
    }

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

    // TODO B4 follow-up: extrair os endpoints de mutação para UseCases dedicados.
    // Hoje: PatchStatus, PatchPlano, GrantTrial, AplicarCupom já usam ports (IAssinatura/
    // IPlano/ICupom/IAuditLog Repository). Restam Impersonate e GetAudit que ainda
    // tocam db.UsuariosEmpresas / db.UsuariosPerfis / db.AdminImpersonationLogs /
    // db.AuditLogs direto — JWT generation + queries complexas, requer refactor maior
    // (criar IAdminImpersonationLogRepository + IUsuarioPerfilRepository.GetByUsuarioE...).
    // Acoplamento atual gated por SuperAdmin policy. Leituras complexas já foram
    // migradas para IAdminTenantsQueries.

    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> PatchStatus(Guid id, [FromBody] PatchTenantStatusRequest req)
    {
        var assinatura = await assinaturaRepo.GetMaisRecenteAsync(id);
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

        await assinaturaRepo.UpdateAsync(assinatura);
        await auditLogRepo.AddAsync(AuditLog.Criar(
            currentUser.UsuarioId,
            $"AdminAlterarStatusTenant:{novoStatus}",
            true,
            $"EmpresaId={id}. Motivo: {req.Motivo}",
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            null));

        await unitOfWork.CommitAsync();
        await audit.LogAsync("TenantStatusAlterado", $"Status={novoStatus}, Motivo={req.Motivo}", id);
        return DataOk(new { status = novoStatus.ToString() });
    }

    [HttpPost("{id:guid}/impersonate")]
    public async Task<IActionResult> Impersonate(Guid id)
    {
        var empresa = await db.Empresas.FindAsync(id);
        if (empresa is null) return DataNotFound("Tenant não encontrado.");

        // Busca usuário ativo + perfil/nivel nesta empresa em uma única query.
        // EF traduz a subquery em Select pra LEFT JOIN; antes era 2 round-trips.
        var dados = await db.UsuariosEmpresas
            .Where(x => x.EmpresaId == id && x.Usuario!.Ativo)
            .OrderBy(x => x.CriadoEm)
            .Select(x => new
            {
                Usuario = x.Usuario!,
                Nivel = db.UsuariosPerfis
                    .Where(up => up.UsuarioId == x.UsuarioId && up.EmpresaId == id)
                    .Select(up => (NivelAcesso?)up.Perfil!.Nivel)
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync();

        if (dados?.Usuario is null)
            return DataNotFound("Nenhum usuário ativo encontrado nesta empresa.");

        // Cap at Admin — impersonation must never produce a SuperAdmin token
        var rawNivel = dados.Nivel ?? NivelAcesso.Admin;
        var nivel = rawNivel == NivelAcesso.SuperAdmin ? NivelAcesso.Admin : rawNivel;

        var secretKey = configuration["Jwt:SecretKey"];
        if (string.IsNullOrEmpty(secretKey))
            return DataBadRequest("Configuração JWT ausente.", "Jwt:SecretKey não configurado.");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new("sub", dados.Usuario.Id.ToString()),
            new("email", dados.Usuario.Email),
            new("nome", dados.Usuario.Nome),
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
        var assinatura = await assinaturaRepo.GetAtivaMaisRecenteAsync(id);
        if (assinatura is null) return DataNotFound("Assinatura ativa não encontrada.");

        var plano = await planoRepo.GetByIdAsync(req.PlanoId);
        if (plano is null) return DataNotFound("Plano não encontrado.");

        assinatura.PlanoId = req.PlanoId;
        assinatura.AlteradoEm = DateTime.UtcNow;
        await assinaturaRepo.UpdateAsync(assinatura);
        await unitOfWork.CommitAsync();
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

        var assinatura = await assinaturaRepo.GetMaisRecenteAsync(id);
        if (assinatura is null) return DataNotFound("Assinatura não encontrada.");

        assinatura.AtivarTrial(req.DiasTrial);
        await assinaturaRepo.UpdateAsync(assinatura);
        await unitOfWork.CommitAsync();
        await audit.LogAsync("TrialConcedido", $"Dias={req.DiasTrial}, TrialFim={assinatura.TrialFim:O}", id);

        return DataOk(new { trialFim = assinatura.TrialFim });
    }

    [HttpPost("{id:guid}/aplicar-cupom")]
    public async Task<IActionResult> AplicarCupom(Guid id, [FromBody] AplicarCupomRequest req)
    {
        var assinatura = await assinaturaRepo.GetMaisRecenteAsync(id);
        if (assinatura is null) return DataNotFound("Assinatura não encontrada.");

        var cupom = await cupomRepo.GetByCodigoAsync(req.Codigo);
        if (cupom is null) return DataNotFound("Cupom não encontrado.");

        if (!cupom.PodeUsarEm(DateTime.UtcNow))
            return Conflict(new { error = new { code = "CUPOM_INVALIDO", message = "Cupom inválido, expirado ou esgotado." } });

        assinatura.AplicarCupom(cupom);
        await assinaturaRepo.UpdateAsync(assinatura);
        await unitOfWork.CommitAsync();
        await audit.LogAsync("CupomAplicado", $"Codigo={cupom.Codigo}, Desconto={cupom.Valor}", id);

        return DataOk(new { cupomCodigo = cupom.Codigo, descontoAplicado = cupom.Valor });
    }

    private static bool ValidarMotivo(string? motivo, out string motivoNormalizado, out string? erro)
    {
        motivoNormalizado = (motivo ?? string.Empty).Trim();
        if (motivoNormalizado.Length < MotivoMinimo)
        {
            erro = $"Justificativa obrigatória (mínimo {MotivoMinimo} caracteres) — fica registrada no audit log.";
            return false;
        }
        if (motivoNormalizado.Length > 1000)
        {
            erro = "Justificativa muito longa (máx 1000 caracteres).";
            return false;
        }
        erro = null;
        return true;
    }

    private static string MascararEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "(vazio)";
        var at = email.IndexOf('@');
        if (at <= 0 || at == email.Length - 1) return "***";
        return email[0] + "***@" + email[(at + 1)..];
    }
}

public record PatchTenantStatusRequest(string Status, string? Motivo);
public record PatchTenantPlanoRequest(Guid PlanoId);
public record GrantTrialRequest(int DiasTrial);
public record AplicarCupomRequest(string Codigo);
public record CriarTenantManualRequest(
    string Motivo,
    string? NomeEmpresa,
    string? Documento,
    string? NomeAdmin,
    string? EmailAdmin,
    bool? EnviarEmail);

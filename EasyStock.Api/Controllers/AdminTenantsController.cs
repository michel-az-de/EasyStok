using EasyStock.Application.UseCases.Admin.CriarTenantPorAdmin;
using EasyStock.Application.UseCases.Admin.ExportarTenantsCsv;
using EasyStock.Application.UseCases.Common;
using EasyStock.Infra.Postgre.Data;
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
    ExportarTenantsCsvUseCase exportarTenantsCsvUseCase,
    ILogger<AdminTenantsController> logger) : EasyStockControllerBase
{
    /// <summary>Exporta clientes filtrados (ou os <c>ids</c> selecionados) como CSV.</summary>
    [HttpGet("export.csv")]
    public async Task<IActionResult> ExportarCsv(
        [FromQuery] string? search,
        [FromQuery] StatusAssinatura? status,
        [FromQuery] List<Guid>? ids,
        CancellationToken ct = default)
    {
        var bytes = await exportarTenantsCsvUseCase.ExecuteAsync(
            new ExportarTenantsCsvCommand(search, status, ids), ct);
        var ts = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        return File(bytes, "text/csv; charset=utf-8", $"clientes-{ts}.csv");
    }
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
        if (!RequestGuards.TryValidarMotivo(req?.Motivo, out var motivo, out var erro)) return DataBadRequest(erro!);

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

    /// <summary>
    /// F4 — Health check da sincronizacao mobile pra esse tenant. Retorna
    /// contagens de mobile_* sem o respectivo erp_*_id, mostrando o gap de
    /// sync. Operador SuperAdmin usa pra detectar pedidos travados/perda
    /// de dados antes do cliente reclamar.
    ///
    /// Todos os contadores em 0 = sincronia 100%.
    /// </summary>
    [HttpGet("{id:guid}/mobile-sync-health")]
    public async Task<IActionResult> GetMobileSyncHealth(Guid id)
    {
        // Usa SQL raw pra evitar carregar entidades — leitura de contagens
        // em tabelas mobile_*. IgnoreQueryFilters: SuperAdmin policy.
        var pendingOrders = await db.Set<Domain.Entities.Mobile.Order>().IgnoreQueryFilters()
            .CountAsync(o => o.EmpresaId == id && o.ErpPedidoId == null);
        var entregueSemVenda = await db.Set<Domain.Entities.Mobile.Order>().IgnoreQueryFilters()
            .CountAsync(o => o.EmpresaId == id && o.Status == "entregue" && o.ErpVendaId == null);
        var pendingClients = await db.Set<Domain.Entities.Mobile.Client>().IgnoreQueryFilters()
            .CountAsync(c => c.EmpresaId == id && c.ErpClienteId == null);
        var pendingProducts = await db.Set<Domain.Entities.Mobile.Product>().IgnoreQueryFilters()
            .CountAsync(p => p.EmpresaId == id && p.ErpProductId == null);
        var pendingBatches = await db.Set<Domain.Entities.Mobile.Batch>().IgnoreQueryFilters()
            .CountAsync(b => b.EmpresaId == id && b.ErpLoteId == null);
        var pendingCash = await db.Set<Domain.Entities.Mobile.CashEntry>().IgnoreQueryFilters()
            .CountAsync(c => c.EmpresaId == id && c.ErpMovimentoCaixaId == null);

        var totalOrders = await db.Set<Domain.Entities.Mobile.Order>().IgnoreQueryFilters()
            .CountAsync(o => o.EmpresaId == id);
        var totalClients = await db.Set<Domain.Entities.Mobile.Client>().IgnoreQueryFilters()
            .CountAsync(c => c.EmpresaId == id);
        var totalProducts = await db.Set<Domain.Entities.Mobile.Product>().IgnoreQueryFilters()
            .CountAsync(p => p.EmpresaId == id);
        var totalBatches = await db.Set<Domain.Entities.Mobile.Batch>().IgnoreQueryFilters()
            .CountAsync(b => b.EmpresaId == id);
        var totalCash = await db.Set<Domain.Entities.Mobile.CashEntry>().IgnoreQueryFilters()
            .CountAsync(c => c.EmpresaId == id);
        var devices = await db.Set<Domain.Entities.Mobile.MobileDevice>().IgnoreQueryFilters()
            .CountAsync(d => d.EmpresaId == id && !d.Revoked);
        var lastSync = await db.Set<Domain.Entities.Mobile.MobileDevice>().IgnoreQueryFilters()
            .Where(d => d.EmpresaId == id)
            .MaxAsync(d => (DateTime?)d.LastSeenAt);

        var totalPending = pendingOrders + entregueSemVenda + pendingClients
                         + pendingProducts + pendingBatches + pendingCash;
        var healthy = totalPending == 0;

        // F10-D: dados expandidos — processed mutations last 24h, audit entries
        var last24h = DateTime.UtcNow.AddHours(-24);
        var processedMutationsLast24h = await db.MobileProcessedMutations
            .AsNoTracking().IgnoreQueryFilters()
            .CountAsync(m => m.EmpresaId == id && m.CriadoEm >= last24h);
        var auditEntriesTotal = await db.EntityAlteracoes
            .AsNoTracking().IgnoreQueryFilters()
            .CountAsync(a => a.EmpresaId == id);

        return DataOk(new
        {
            healthy,
            totalPending,
            lastSyncAt = lastSync,
            devices,
            processedMutationsLast24h,
            auditEntriesTotal,
            orders   = new { total = totalOrders,   pending = pendingOrders,   entregueSemVenda },
            clients  = new { total = totalClients,  pending = pendingClients },
            products = new { total = totalProducts, pending = pendingProducts },
            batches  = new { total = totalBatches,  pending = pendingBatches },
            cash     = new { total = totalCash,     pending = pendingCash }
        });
    }

    /// <summary>
    /// F10-D — Saúde mobile global: semáforo por tenant.
    /// Verde = 0 pending. Amarelo = 1-10 pending. Vermelho = >10 pending ou lastSync >24h.
    /// </summary>
    [HttpGet("mobile-sync-health-global")]
    public async Task<IActionResult> GetMobileSyncHealthGlobal()
    {
        // Empresas com pelo menos 1 device ativo
        var empresaIds = await db.Set<Domain.Entities.Mobile.MobileDevice>()
            .AsNoTracking().IgnoreQueryFilters()
            .Where(d => !d.Revoked)
            .Select(d => d.EmpresaId)
            .Distinct()
            .ToListAsync();

        var now = DateTime.UtcNow;

        // Batch queries — avoid N+1 per empresa
        var empresas = await db.Empresas.AsNoTracking().IgnoreQueryFilters()
            .Where(e => empresaIds.Contains(e.Id))
            .Select(e => new { e.Id, e.Nome })
            .ToDictionaryAsync(e => e.Id);

        var pendingOrdersByEmp = await db.Set<Domain.Entities.Mobile.Order>()
            .AsNoTracking().IgnoreQueryFilters()
            .Where(o => o.EmpresaId.HasValue && empresaIds.Contains(o.EmpresaId.Value) && o.ErpPedidoId == null)
            .GroupBy(o => o.EmpresaId!.Value)
            .Select(g => new { EmpresaId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.EmpresaId, x => x.Count);

        var pendingBatchesByEmp = await db.Set<Domain.Entities.Mobile.Batch>()
            .AsNoTracking().IgnoreQueryFilters()
            .Where(b => b.EmpresaId.HasValue && empresaIds.Contains(b.EmpresaId.Value) && b.ErpLoteId == null)
            .GroupBy(b => b.EmpresaId!.Value)
            .Select(g => new { EmpresaId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.EmpresaId, x => x.Count);

        var pendingCashByEmp = await db.Set<Domain.Entities.Mobile.CashEntry>()
            .AsNoTracking().IgnoreQueryFilters()
            .Where(c => c.EmpresaId.HasValue && empresaIds.Contains(c.EmpresaId.Value) && c.ErpMovimentoCaixaId == null)
            .GroupBy(c => c.EmpresaId!.Value)
            .Select(g => new { EmpresaId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.EmpresaId, x => x.Count);

        var deviceInfoByEmp = await db.Set<Domain.Entities.Mobile.MobileDevice>()
            .AsNoTracking().IgnoreQueryFilters()
            .Where(d => empresaIds.Contains(d.EmpresaId) && !d.Revoked)
            .GroupBy(d => d.EmpresaId)
            .Select(g => new { EmpresaId = g.Key, Count = g.Count(), LastSeen = g.Max(d => (DateTime?)d.LastSeenAt) })
            .ToDictionaryAsync(x => x.EmpresaId);

        var results = new List<object>();
        foreach (var empId in empresaIds)
        {
            if (!empresas.TryGetValue(empId, out var empresa)) continue;

            var pendingOrders = pendingOrdersByEmp.GetValueOrDefault(empId);
            var pendingBatches = pendingBatchesByEmp.GetValueOrDefault(empId);
            var pendingCash = pendingCashByEmp.GetValueOrDefault(empId);
            var totalPending = pendingOrders + pendingBatches + pendingCash;

            deviceInfoByEmp.TryGetValue(empId, out var devInfo);
            var lastSync = devInfo?.LastSeen;
            var deviceCount = devInfo?.Count ?? 0;

            var staleSync = lastSync.HasValue && (now - lastSync.Value).TotalHours > 24;
            var status = totalPending == 0 && !staleSync ? "green"
                       : totalPending <= 10 && !staleSync ? "yellow"
                       : "red";

            results.Add(new
            {
                empresaId = empresa.Id,
                empresaNome = empresa.Nome,
                status,
                totalPending,
                pendingOrders,
                pendingBatches,
                pendingCash,
                lastSyncAt = lastSync,
                deviceCount
            });
        }

        return DataOk(results.OrderByDescending(r => ((dynamic)r).totalPending));
    }

    // TODO B4 follow-up: extrair Impersonate para UseCase dedicado.
    // GetAudit já foi migrado para IAdminTenantsQueries.GetAuditLogsPagedAsync.
    // Impersonate ainda usa db.* direto (UsuariosEmpresas, UsuariosPerfis, AdminImpersonationLogs)
    // + gera JWT inline — requer IAdminImpersonationLogRepository + extração do JWT signing
    // para um ITokenService antes de ser extraível sem aumentar complexidade.

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
        var (logs, total) = await tenantsQueries.GetAuditLogsPagedAsync(id, page, pageSize);
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

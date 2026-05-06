using EasyStock.Api.Http;
using EasyStock.Api.Services;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Admin.AnonimizarUsuarioPorAdmin;
using EasyStock.Application.UseCases.AtualizarLoja;
using EasyStock.Application.UseCases.CriarLoja;
using EasyStock.Application.UseCases.DesativarLoja;
using EasyStock.Application.UseCases.ReativarLoja;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Exceptions;
using EasyStock.Infra.Postgre.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace EasyStock.Api.Controllers;

/// <summary>
/// Endpoints SuperAdmin de gestão de Cliente (tenant). Centraliza CRUD de Lojas
/// e demais ações por tenant que não cabem em <see cref="AdminUsuariosTenantController"/>
/// (ações por usuário) ou <see cref="AdminTenantsController"/> (assinatura/plano).
/// Toda mutação exige `motivo` (≥10 chars) auditado.
/// </summary>
[ApiController]
[Route("api/admin/clientes")]
[Authorize(Policy = "SuperAdmin")]
public class AdminClientesController(
    EasyStockDbContext db,
    ILojaRepository lojaRepo,
    CriarLojaUseCase criarLojaUseCase,
    AtualizarLojaUseCase atualizarLojaUseCase,
    DesativarLojaUseCase desativarLojaUseCase,
    ReativarLojaUseCase reativarLojaUseCase,
    AnonimizarUsuarioPorAdminUseCase anonimizarUseCase,
    AdminAuditService audit,
    IHttpContextAccessor http,
    ILogger<AdminClientesController> logger) : EasyStockControllerBase
{
    private const int MotivoMinimo = 10;

    // ─────────────────────────── #10: Criar loja ───────────────────────────

    [HttpPost("{tenantId:guid}/lojas")]
    public async Task<IActionResult> CriarLoja(Guid tenantId, [FromBody] CriarLojaAdminRequest req)
    {
        if (tenantId == Guid.Empty) return DataBadRequest("Cliente inválido.");
        if (!ValidarMotivo(req?.Motivo, out var motivo, out var erro)) return DataBadRequest(erro!);
        if (string.IsNullOrWhiteSpace(req?.Nome)) return DataBadRequest("Nome da loja é obrigatório.");

        try
        {
            var resultado = await criarLojaUseCase.ExecuteAsync(new CriarLojaCommand(
                EmpresaId: tenantId,
                Nome: req!.Nome,
                Descricao: req.Descricao,
                Documento: req.Documento,
                Endereco: req.Endereco,
                Telefone: req.Telefone));

            await audit.LogAsync(
                "LojaCriada",
                $"LojaId={resultado.Id}, Nome={resultado.Nome}",
                tenantId: tenantId,
                motivo: motivo,
                entidadeAfetadaId: resultado.Id);

            return DataCreated($"/api/admin/clientes/{tenantId}/lojas/{resultado.Id}", resultado);
        }
        catch (PlanoLimiteAtingidoException ex)
        {
            return DataBadRequest($"Limite de lojas do plano atingido. Recurso: {ex.Recurso}.");
        }
        catch (UseCaseValidationException ex)
        {
            return DataBadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao criar loja para tenant {TenantId}", tenantId);
            return Problem(detail: ex.Message, statusCode: 500, title: "Erro ao criar loja.");
        }
    }

    // ─────────────────────────── #11: Atualizar loja (com diff) ───────────────────────────

    [HttpPatch("{tenantId:guid}/lojas/{lojaId:guid}")]
    public async Task<IActionResult> AtualizarLoja(Guid tenantId, Guid lojaId, [FromBody] AtualizarLojaAdminRequest req)
    {
        if (tenantId == Guid.Empty || lojaId == Guid.Empty) return DataBadRequest("Cliente ou loja inválidos.");
        if (!ValidarMotivo(req?.Motivo, out var motivo, out var erro)) return DataBadRequest(erro!);
        if (string.IsNullOrWhiteSpace(req?.Nome)) return DataBadRequest("Nome da loja é obrigatório.");

        // Carrega antes pra calcular diff before/after.
        var antes = await lojaRepo.GetByIdAsync(tenantId, lojaId);
        if (antes is null) return DataNotFound("Loja não encontrada neste cliente.");

        var alteracoes = new List<string>();
        if (!string.Equals(antes.Nome, req!.Nome.Trim(), StringComparison.Ordinal))
            alteracoes.Add($"nome: '{antes.Nome}' → '{req.Nome.Trim()}'");
        if (!string.Equals(antes.Descricao ?? "", (req.Descricao ?? "").Trim(), StringComparison.Ordinal))
            alteracoes.Add($"descricao alterada");
        if (!string.Equals(antes.Documento ?? "", (req.Documento ?? "").Trim(), StringComparison.Ordinal))
            alteracoes.Add($"documento: '{antes.Documento ?? "-"}' → '{req.Documento ?? "-"}'");
        if (!string.Equals(antes.Endereco ?? "", (req.Endereco ?? "").Trim(), StringComparison.Ordinal))
            alteracoes.Add("endereco alterado");
        if (!string.Equals(antes.Telefone ?? "", (req.Telefone ?? "").Trim(), StringComparison.Ordinal))
            alteracoes.Add($"telefone: '{antes.Telefone ?? "-"}' → '{req.Telefone ?? "-"}'");

        try
        {
            await atualizarLojaUseCase.ExecuteAsync(new AtualizarLojaCommand(
                LojaId: lojaId,
                EmpresaId: tenantId,
                Nome: req.Nome,
                Descricao: req.Descricao,
                Documento: req.Documento,
                Endereco: req.Endereco,
                Telefone: req.Telefone));
        }
        catch (UseCaseValidationException ex) { return DataBadRequest(ex.Message); }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao atualizar loja {LojaId} do tenant {TenantId}", lojaId, tenantId);
            return Problem(detail: ex.Message, statusCode: 500);
        }

        await audit.LogAsync(
            "LojaAtualizada",
            alteracoes.Count > 0 ? string.Join("; ", alteracoes) : "sem mudanças efetivas",
            tenantId: tenantId,
            motivo: motivo,
            entidadeAfetadaId: lojaId);

        return DataOk(new { lojaId, alterado = alteracoes.Count > 0, alteracoes });
    }

    // ─────────────────────────── #12: Toggle ativa/desativa ───────────────────────────

    [HttpPost("{tenantId:guid}/lojas/{lojaId:guid}/toggle")]
    public async Task<IActionResult> ToggleLoja(Guid tenantId, Guid lojaId, [FromBody] ToggleLojaRequest req)
    {
        if (tenantId == Guid.Empty || lojaId == Guid.Empty) return DataBadRequest("Cliente ou loja inválidos.");
        if (!ValidarMotivo(req?.Motivo, out var motivo, out var erro)) return DataBadRequest(erro!);

        var loja = await lojaRepo.GetByIdAsync(tenantId, lojaId);
        if (loja is null) return DataNotFound("Loja não encontrada neste cliente.");

        try
        {
            if (req!.Ativa && !loja.Ativa)
            {
                await reativarLojaUseCase.ExecuteAsync(new ReativarLojaCommand(lojaId, tenantId));
                await audit.LogAsync("LojaReativada", $"LojaId={lojaId}, Nome={loja.Nome}",
                    tenantId, motivo: motivo, entidadeAfetadaId: lojaId);
                return DataOk(new { lojaId, ativa = true });
            }
            if (!req.Ativa && loja.Ativa)
            {
                await desativarLojaUseCase.ExecuteAsync(new DesativarLojaCommand(lojaId, tenantId));
                await audit.LogAsync("LojaDesativada", $"LojaId={lojaId}, Nome={loja.Nome}",
                    tenantId, motivo: motivo, entidadeAfetadaId: lojaId);
                return DataOk(new { lojaId, ativa = false });
            }
            // Estado já é o desejado — idempotente.
            return DataOk(new { lojaId, ativa = loja.Ativa, mensagem = "Loja já estava neste estado." });
        }
        catch (UseCaseValidationException ex) { return DataBadRequest(ex.Message); }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao alternar loja {LojaId} do tenant {TenantId}", lojaId, tenantId);
            return Problem(detail: ex.Message, statusCode: 500);
        }
    }

    // ─────────────────── #13: Atividade unificada (timeline) ───────────────────

    /// <summary>
    /// Mescla AuditLog (atividade do tenant: logins, vendas, alterações dos
    /// próprios usuários) + AdminAuditLog (ações do operador SuperAdmin sobre
    /// o tenant) em uma timeline única, ordenada por data desc, com filtros.
    /// Mascaramento de PII via PiiMaskingHelper-style nas mensagens.
    /// </summary>
    [HttpGet("{tenantId:guid}/atividade")]
    public async Task<IActionResult> GetAtividade(
        Guid tenantId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30,
        [FromQuery] string? tipo = null,        // "tenant" | "admin" | null=todos
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] Guid? usuarioId = null,
        [FromQuery] string? search = null)
    {
        if (tenantId == Guid.Empty) return DataBadRequest("Cliente inválido.");
        (page, pageSize) = NormalisePage(page, pageSize, maxPageSize: 100);

        var fromUtc = from?.ToUniversalTime();
        var toUtc = to?.ToUniversalTime();

        // === Tenant audit (AuditLog) — usuários da empresa ===
        var usuariosIds = await db.UsuariosEmpresas
            .Where(ue => ue.EmpresaId == tenantId)
            .Select(ue => ue.UsuarioId)
            .ToListAsync();

        var tenantQuery = db.AuditLogs.AsNoTracking()
            .Where(a => usuariosIds.Contains(a.UsuarioId));
        if (fromUtc.HasValue) tenantQuery = tenantQuery.Where(a => a.DataHora >= fromUtc.Value);
        if (toUtc.HasValue) tenantQuery = tenantQuery.Where(a => a.DataHora <= toUtc.Value);
        if (usuarioId.HasValue) tenantQuery = tenantQuery.Where(a => a.UsuarioId == usuarioId.Value);
        if (!string.IsNullOrWhiteSpace(search))
            tenantQuery = tenantQuery.Where(a => EF.Functions.ILike(a.Acao, $"%{search}%") || (a.Detalhes != null && EF.Functions.ILike(a.Detalhes, $"%{search}%")));

        // === Admin audit (AdminAuditLog) — ações do operador no tenant ===
        var adminQuery = db.AdminAuditLogs.AsNoTracking()
            .Where(a => a.TenantId == tenantId);
        if (fromUtc.HasValue) adminQuery = adminQuery.Where(a => a.CriadoEm >= fromUtc.Value);
        if (toUtc.HasValue) adminQuery = adminQuery.Where(a => a.CriadoEm <= toUtc.Value);
        if (usuarioId.HasValue) adminQuery = adminQuery.Where(a => a.EntidadeAfetadaId == usuarioId.Value);
        if (!string.IsNullOrWhiteSpace(search))
            adminQuery = adminQuery.Where(a => EF.Functions.ILike(a.Acao, $"%{search}%") || (a.Detalhes != null && EF.Functions.ILike(a.Detalhes, $"%{search}%")));

        // Aplica filtro de tipo (tenant/admin) só na contagem e pegada de itens.
        var listarTenant = string.IsNullOrEmpty(tipo) || tipo == "tenant";
        var listarAdmin = string.IsNullOrEmpty(tipo) || tipo == "admin";

        var totalTenant = listarTenant ? await tenantQuery.CountAsync() : 0;
        var totalAdmin = listarAdmin ? await adminQuery.CountAsync() : 0;
        var total = totalTenant + totalAdmin;

        // Estratégia: pegar até `page * pageSize` de cada lado, mesclar, ordenar e paginar.
        // Em P1 isso é OK; em P2 pode virar query SQL nativa com UNION ALL pra eficiência.
        var pegada = page * pageSize;
        var tenantItems = listarTenant
            ? await tenantQuery.OrderByDescending(a => a.DataHora).Take(pegada)
                .Select(a => new { a.Id, tipo = "tenant", a.Acao, sucesso = (bool?)a.Sucesso, a.Detalhes, a.Ip, dataHora = a.DataHora, ator = (Guid?)a.UsuarioId, motivo = (string?)null })
                .ToListAsync()
            : new();
        var adminItems = listarAdmin
            ? await adminQuery.OrderByDescending(a => a.CriadoEm).Take(pegada)
                .Select(a => new { a.Id, tipo = "admin", a.Acao, sucesso = (bool?)null, a.Detalhes, a.Ip, dataHora = a.CriadoEm, ator = (Guid?)null, motivo = a.Motivo })
                .ToListAsync()
            : new();

        var nomesUsuarios = usuariosIds.Count > 0
            ? await db.Usuarios.AsNoTracking()
                .Where(u => usuariosIds.Contains(u.Id))
                .Select(u => new { u.Id, u.Nome, u.Email })
                .ToDictionaryAsync(u => u.Id, u => new { u.Nome, EmailMascarado = MascararEmail(u.Email) })
            : new();

        var merged = tenantItems
            .Select(a => new
            {
                id = a.Id,
                tipo = a.tipo,
                acao = a.Acao,
                sucesso = a.sucesso,
                detalhes = MascararDetalhes(a.Detalhes),
                ip = MascararIp(a.Ip),
                dataHora = a.dataHora,
                ator = a.ator?.ToString(),
                atorNome = a.ator.HasValue && nomesUsuarios.TryGetValue(a.ator.Value, out var u) ? u.Nome : "Usuário",
                atorEmail = a.ator.HasValue && nomesUsuarios.TryGetValue(a.ator.Value, out var u2) ? u2.EmailMascarado : null,
                motivo = (string?)null
            })
            .Concat(adminItems.Select(a => new
            {
                id = a.Id,
                tipo = a.tipo,
                acao = a.Acao,
                sucesso = a.sucesso,
                detalhes = MascararDetalhes(a.Detalhes),
                ip = MascararIp(a.Ip),
                dataHora = a.dataHora,
                ator = (string?)null,
                atorNome = "Operador admin",
                atorEmail = (string?)null,
                motivo = a.motivo
            }))
            .OrderByDescending(a => a.dataHora)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return DataPaged(merged, total, page, pageSize);
    }

    // ─────────────────── #9: PII unmask just-in-time ───────────────────

    /// <summary>
    /// Devolve um campo PII em texto-claro de uma entidade do tenant. Cada chamada
    /// gera linha em <see cref="AdminAcessoPiiLog"/> (LGPD Art. 37 — registro de
    /// tratamento). UI deve mostrar o valor por janela curta (5min) e depois
    /// remascarar via timer no cliente.
    /// </summary>
    [HttpGet("{tenantId:guid}/usuario-pii/{userId:guid}")]
    public async Task<IActionResult> GetUsuarioPii(
        Guid tenantId,
        Guid userId,
        [FromQuery] string campo,
        [FromQuery] string motivo)
    {
        if (tenantId == Guid.Empty || userId == Guid.Empty)
            return DataBadRequest("Cliente ou usuário inválidos.");
        if (!ValidarMotivo(motivo, out var motivoT, out var erro))
            return DataBadRequest(erro!);

        var campoNormalizado = (campo ?? "").Trim().ToLowerInvariant();
        if (campoNormalizado is not ("email" or "documento" or "telefone"))
            return DataBadRequest("Campo inválido. Use: email | documento | telefone.");

        // Valida que o usuário pertence ao tenant.
        var usuarioPertence = await db.UsuariosEmpresas
            .AnyAsync(ue => ue.UsuarioId == userId && ue.EmpresaId == tenantId);
        if (!usuarioPertence) return DataNotFound("Usuário não pertence a este cliente.");

        var usuario = await db.Usuarios.FindAsync(userId);
        if (usuario is null) return DataNotFound("Usuário não encontrado.");

        // Por enquanto Usuario só tem Email (não tem documento/telefone próprios).
        // Documentos/telefones costumam estar em Empresa/Loja — aqui mantemos só email
        // pra não vazar mais do que o necessário. Telefone/Documento ficam pra futuro.
        string valor = campoNormalizado switch
        {
            "email" => usuario.Email,
            _ => "(campo ainda não disponível)"
        };

        // Audit dedicado pra PII (separado de AdminAuditLog técnico — facilita relatório ANPD).
        var ctx = http.HttpContext;
        var adminEmail = ctx?.User?.FindFirstValue(ClaimTypes.Email)
                         ?? ctx?.User?.FindFirstValue("email")
                         ?? "anonymous";
        var ip = ctx?.Connection.RemoteIpAddress?.ToString();
        db.AdminAcessosPiiLogs.Add(AdminAcessoPiiLog.Criar(
            adminEmail: adminEmail,
            entidadeTipo: "usuario",
            entidadeId: userId,
            campo: campoNormalizado,
            motivo: motivoT,
            tenantId: tenantId,
            ip: ip));
        await db.CommitAsync();

        // Também grava no AdminAuditLog pra aparecer na timeline ("operador desmascarou email do usuário X").
        await audit.LogAsync(
            "PiiVisualizado",
            $"Campo={campoNormalizado}, UserId={userId}",
            tenantId: tenantId,
            motivo: motivoT,
            entidadeAfetadaId: userId);

        return DataOk(new
        {
            campo = campoNormalizado,
            valor,
            // TTL pra UI re-mascarar automaticamente.
            expiraEm = DateTime.UtcNow.AddMinutes(5)
        });
    }

    // ─────────────────── #8: Anonimizar usuário pelo admin (LGPD) ───────────────────

    [HttpPost("{tenantId:guid}/usuarios/{userId:guid}/anonimizar")]
    public async Task<IActionResult> AnonimizarUsuario(Guid tenantId, Guid userId, [FromBody] AnonimizarUsuarioRequest req)
    {
        if (tenantId == Guid.Empty || userId == Guid.Empty) return DataBadRequest("Cliente ou usuário inválidos.");
        if (string.IsNullOrWhiteSpace(req?.Motivo) || req.Motivo.Trim().Length < 20)
            return DataBadRequest("Justificativa obrigatória (≥20 caracteres) — anonimização é irreversível.");
        if (string.IsNullOrWhiteSpace(req.ConfirmacaoEmail))
            return DataBadRequest("Confirmação de email obrigatória.");

        // Valida pertencimento ao tenant.
        var pertence = await db.UsuariosEmpresas.AnyAsync(ue => ue.UsuarioId == userId && ue.EmpresaId == tenantId);
        if (!pertence) return DataNotFound("Usuário não pertence a este cliente.");

        try
        {
            var resultado = await anonimizarUseCase.ExecuteAsync(new AnonimizarUsuarioPorAdminCommand(
                UsuarioId: userId,
                ConfirmacaoEmail: req.ConfirmacaoEmail,
                Motivo: req.Motivo));

            await audit.LogAsync(
                "UsuarioTenantAnonimizado",
                $"UserId={userId}; tokens removidos refresh:{resultado.RefreshTokensRemovidos} reset:{resultado.ResetTokensRemovidos} confirm:{resultado.EmailConfirmationTokensRemovidos}",
                tenantId: tenantId,
                motivo: req.Motivo,
                entidadeAfetadaId: userId);

            return DataOk(new
            {
                usuarioId = resultado.UsuarioId,
                anonimizadoEm = resultado.AnonimizadoEm,
                tokensRemovidos = new
                {
                    refresh = resultado.RefreshTokensRemovidos,
                    reset = resultado.ResetTokensRemovidos,
                    confirmacaoEmail = resultado.EmailConfirmationTokensRemovidos
                },
                mensagem = "Usuário anonimizado. Operação irreversível registrada no audit LGPD."
            });
        }
        catch (UseCaseValidationException ex) { return DataBadRequest(ex.Message); }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao anonimizar usuário {UserId} do tenant {TenantId}", userId, tenantId);
            return Problem(detail: ex.Message, statusCode: 500, title: "Erro ao anonimizar.");
        }
    }

    // ─────────────────── Exportar dados do tenant (LGPD) ───────────────────

    /// <summary>
    /// LGPD Art. 18 — direito de portabilidade. Snapshot estruturado dos dados do
    /// tenant: empresa, lojas, usuários (mascarados), assinatura, contagens. Download
    /// como JSON estruturado. ZIP fica como evolução futura (incluir movimentações).
    /// </summary>
    [HttpGet("{tenantId:guid}/exportar")]
    public async Task<IActionResult> ExportarDados(Guid tenantId, [FromQuery] string motivo)
    {
        if (tenantId == Guid.Empty) return DataBadRequest("Cliente inválido.");
        if (!ValidarMotivo(motivo, out var motivoT, out var erro)) return DataBadRequest(erro!);

        var empresa = await db.Empresas.AsNoTracking().FirstOrDefaultAsync(e => e.Id == tenantId);
        if (empresa is null) return DataNotFound("Cliente não encontrado.");

        var lojas = await db.Lojas.AsNoTracking()
            .Where(l => l.EmpresaId == tenantId)
            .Select(l => new { l.Id, l.Nome, l.Descricao, l.Documento, l.Endereco, l.Telefone, l.Ativa, l.CriadoEm, l.AlteradoEm })
            .ToListAsync();

        var usuariosIds = await db.UsuariosEmpresas
            .Where(ue => ue.EmpresaId == tenantId)
            .Select(ue => ue.UsuarioId)
            .ToListAsync();

        var usuarios = await db.Usuarios.AsNoTracking()
            .Where(u => usuariosIds.Contains(u.Id))
            .Select(u => new
            {
                u.Id,
                u.Nome,
                emailMascarado = MascararEmail(u.Email),
                u.Ativo,
                u.EmailConfirmado,
                u.UltimoAcessoEm,
                u.CriadoEm,
                u.AlteradoEm
            })
            .ToListAsync();

        var snapshot = new
        {
            geradoEm = DateTime.UtcNow,
            geradoPor = http.HttpContext?.User?.FindFirstValue(ClaimTypes.Email) ?? "anonymous",
            motivo = motivoT,
            tenantId,
            lgpdNote = "Snapshot LGPD Art. 18 — emails mascarados. Use endpoint /usuario-pii/{id} pra desmascarar individualmente (auditado).",
            empresa = new { empresa.Id, empresa.Nome, empresa.Documento, empresa.CriadoEm, empresa.AlteradoEm },
            contagens = new
            {
                lojas = lojas.Count,
                lojasAtivas = lojas.Count(l => l.Ativa),
                usuarios = usuarios.Count,
                usuariosAtivos = usuarios.Count(u => u.Ativo)
            },
            lojas,
            usuarios
        };

        await audit.LogAsync(
            "TenantDadosExportados",
            $"Empresa={empresa.Nome}, Lojas={lojas.Count}, Usuarios={usuarios.Count}",
            tenantId: tenantId,
            motivo: motivoT,
            entidadeAfetadaId: tenantId);

        // JSON download — Content-Disposition força attachment.
        var fileName = $"easystock-export-tenant-{tenantId:N}-{DateTime.UtcNow:yyyyMMddHHmmss}.json";
        var json = System.Text.Json.JsonSerializer.Serialize(snapshot, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        return File(System.Text.Encoding.UTF8.GetBytes(json), "application/json", fileName);
    }

    // ─────────────────── Histórico LGPD do tenant ───────────────────

    /// <summary>
    /// Lista ações LGPD-relevantes feitas neste tenant pelo admin (anonimização,
    /// export, PII unmask). Mostra quem fez, quando, motivo. Fonte para tab
    /// "Histórico LGPD" — comprovação à ANPD em disputas ou auditorias.
    /// </summary>
    [HttpGet("{tenantId:guid}/lgpd/historico")]
    public async Task<IActionResult> HistoricoLgpd(
        Guid tenantId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30)
    {
        if (tenantId == Guid.Empty) return DataBadRequest("Cliente inválido.");
        (page, pageSize) = NormalisePage(page, pageSize, maxPageSize: 100);

        // Ações LGPD-relevantes que aparecem na timeline geral, mas precisam de
        // visualização dedicada pra relatório. Whitelist mantém o filtro estrito.
        var acoesLgpd = new[] { "UsuarioTenantAnonimizado", "PiiVisualizado", "TenantDadosExportados" };

        var query = db.AdminAuditLogs.AsNoTracking()
            .Where(a => a.TenantId == tenantId && acoesLgpd.Contains(a.Acao));
        var total = await query.CountAsync();
        var itens = await query
            .OrderByDescending(a => a.CriadoEm)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new
            {
                a.Id,
                a.Acao,
                a.AdminEmail,
                a.Detalhes,
                a.Motivo,
                a.EntidadeAfetadaId,
                criadoEm = a.CriadoEm
            })
            .ToListAsync();

        // Estatísticas rápidas pra mostrar no card.
        var totalUnmasks = await db.AdminAcessosPiiLogs.AsNoTracking()
            .Where(p => p.TenantId == tenantId)
            .CountAsync();

        // Envelope custom (não usa DataPaged) pra preservar `estatisticas` na resposta.
        // DataPaged só aceita IEnumerable<T> + meta — perderia o objeto extra.
        var pages = pageSize > 0 ? (int)Math.Ceiling((double)total / pageSize) : 0;
        return Ok(new
        {
            data = itens,
            meta = new { total, pages, page, limit = pageSize },
            estatisticas = new
            {
                totalAcoesLgpd = total,
                totalUnmasks
            }
        });
    }

    // ─────────────────── #14: Notas internas (handoff entre operadores) ───────────────────

    [HttpGet("{tenantId:guid}/notas")]
    public async Task<IActionResult> ListarNotas(Guid tenantId)
    {
        if (tenantId == Guid.Empty) return DataBadRequest("Cliente inválido.");

        var notas = await db.AdminNotasTenant.AsNoTracking()
            .Where(n => n.TenantId == tenantId && n.ExcluidoEm == null)
            .OrderByDescending(n => n.CriadoEm)
            .Select(n => new
            {
                id = n.Id,
                texto = n.Texto,
                tipo = n.Tipo.ToString(),
                tipoInt = (int)n.Tipo,
                autorAdminId = n.AutorAdminId,
                autorEmail = n.AutorEmail,
                criadoEm = n.CriadoEm,
                alteradoEm = n.AlteradoEm,
                editado = n.AlteradoEm > n.CriadoEm.AddSeconds(2)
            })
            .ToListAsync();

        // Conta alertas pra usar no banner do header (UI faz outro GET /alertas-count
        // se quiser; aqui já vai inline pra economizar 1 round-trip).
        var alertasAtivos = notas.Count(n => n.tipoInt == (int)Domain.Entities.TipoNotaTenant.Alerta);

        return DataOk(new { itens = notas, total = notas.Count, alertasAtivos });
    }

    [HttpPost("{tenantId:guid}/notas")]
    public async Task<IActionResult> CriarNota(Guid tenantId, [FromBody] NotaTenantRequest req)
    {
        if (tenantId == Guid.Empty) return DataBadRequest("Cliente inválido.");
        if (!ValidarTextoNota(req?.Texto, out var textoT, out var erro)) return DataBadRequest(erro!);
        if (!Enum.TryParse<Domain.Entities.TipoNotaTenant>(req!.Tipo, out var tipo))
            return DataBadRequest("Tipo inválido. Use Info, Alerta ou Escalonamento.");

        var (autorId, autorEmail) = ResolverAutor();
        var nota = Domain.Entities.AdminNotaTenant.Criar(tenantId, autorId, autorEmail, textoT, tipo);
        db.AdminNotasTenant.Add(nota);
        await db.CommitAsync();

        await audit.LogAsync(
            "NotaInternaCriada",
            $"NotaId={nota.Id}, Tipo={tipo}, Tamanho={textoT.Length}",
            tenantId: tenantId,
            entidadeAfetadaId: nota.Id);

        return DataCreated($"/api/admin/clientes/{tenantId}/notas/{nota.Id}", new
        {
            id = nota.Id, criadoEm = nota.CriadoEm, autorEmail = nota.AutorEmail
        });
    }

    [HttpPatch("{tenantId:guid}/notas/{notaId:guid}")]
    public async Task<IActionResult> AtualizarNota(Guid tenantId, Guid notaId, [FromBody] NotaTenantRequest req)
    {
        if (tenantId == Guid.Empty || notaId == Guid.Empty) return DataBadRequest("Identificadores inválidos.");
        if (!ValidarTextoNota(req?.Texto, out var textoT, out var erro)) return DataBadRequest(erro!);
        if (!Enum.TryParse<Domain.Entities.TipoNotaTenant>(req!.Tipo, out var tipo))
            return DataBadRequest("Tipo inválido.");

        var nota = await db.AdminNotasTenant.FirstOrDefaultAsync(n => n.Id == notaId && n.TenantId == tenantId && n.ExcluidoEm == null);
        if (nota is null) return DataNotFound("Nota não encontrada.");

        var (autorId, _) = ResolverAutor();
        // Só o autor pode editar (regra de domínio simples — SuperAdmin pode excluir
        // qualquer, mas editar de outro autor mudaria a autoria do registro).
        if (nota.AutorAdminId != autorId)
            return DataBadRequest("Apenas o autor pode editar esta nota. Use exclusão se precisar substituir.");

        nota.Atualizar(textoT, tipo);
        await db.CommitAsync();

        await audit.LogAsync(
            "NotaInternaAtualizada",
            $"NotaId={notaId}, Tipo={tipo}",
            tenantId: tenantId,
            entidadeAfetadaId: notaId);

        return DataOk(new { id = nota.Id, alteradoEm = nota.AlteradoEm });
    }

    [HttpDelete("{tenantId:guid}/notas/{notaId:guid}")]
    public async Task<IActionResult> ExcluirNota(Guid tenantId, Guid notaId)
    {
        if (tenantId == Guid.Empty || notaId == Guid.Empty) return DataBadRequest("Identificadores inválidos.");

        var nota = await db.AdminNotasTenant.FirstOrDefaultAsync(n => n.Id == notaId && n.TenantId == tenantId && n.ExcluidoEm == null);
        if (nota is null) return DataNotFound("Nota não encontrada.");

        // SuperAdmin pode excluir qualquer nota — política mais flexível pra moderação.
        // Soft-delete preserva histórico LGPD do que foi escrito por quem.
        nota.Excluir();
        await db.CommitAsync();

        await audit.LogAsync(
            "NotaInternaExcluida",
            $"NotaId={notaId}, AutorOriginal={nota.AutorEmail}",
            tenantId: tenantId,
            entidadeAfetadaId: notaId);

        return DataOk(new { id = notaId, excluido = true });
    }

    private (Guid AutorId, string AutorEmail) ResolverAutor()
    {
        var ctx = http.HttpContext;
        var email = ctx?.User?.FindFirstValue(ClaimTypes.Email)
                    ?? ctx?.User?.FindFirstValue("email")
                    ?? "anonymous";
        var subClaim = ctx?.User?.FindFirstValue(ClaimTypes.NameIdentifier)
                       ?? ctx?.User?.FindFirstValue("sub");
        Guid.TryParse(subClaim, out var autorId);
        return (autorId, email);
    }

    private static bool ValidarTextoNota(string? texto, out string textoNormalizado, out string? erro)
    {
        textoNormalizado = (texto ?? string.Empty).Trim();
        if (textoNormalizado.Length is < 3 or > 2000)
        {
            erro = "Texto da nota deve ter entre 3 e 2000 caracteres.";
            return false;
        }
        erro = null;
        return true;
    }

    // ─────────────────── helpers de mascaramento ───────────────────

    private static string? MascararDetalhes(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return texto;
        // Mascara emails inline: foo@bar.com → f***@bar.com (preserva domínio pra debug).
        var mascarado = System.Text.RegularExpressions.Regex.Replace(
            texto,
            @"(?<local>[A-Za-z0-9._%+-]+)@(?<domain>[A-Za-z0-9.-]+\.[A-Za-z]{2,})",
            m => m.Groups["local"].Value[0] + "***@" + m.Groups["domain"].Value);
        return mascarado;
    }

    private static string? MascararIp(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return null;
        var partes = ip.Split('.');
        if (partes.Length == 4) return $"{partes[0]}.{partes[1]}.*.*";
        return ip.Length > 8 ? string.Concat(ip.AsSpan(0, ip.Length - 8), "********") : "***";
    }

    private static string MascararEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "(vazio)";
        var at = email.IndexOf('@');
        if (at <= 0 || at == email.Length - 1) return "***";
        return email[0] + "***@" + email[(at + 1)..];
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
}

public record CriarLojaAdminRequest(string Motivo, string Nome, string? Descricao, string? Documento, string? Endereco, string? Telefone);
public record AtualizarLojaAdminRequest(string Motivo, string Nome, string? Descricao, string? Documento, string? Endereco, string? Telefone);
public record ToggleLojaRequest(string Motivo, bool Ativa);
public record AnonimizarUsuarioRequest(string Motivo, string ConfirmacaoEmail);
public record NotaTenantRequest(string Texto, string Tipo);

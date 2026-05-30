using EasyStock.Application.UseCases.Admin.CriarUsuarioTenantPorAdmin;
using EasyStock.Application.UseCases.Common;
using EasyStock.Infra.Postgre.Data;
using System.Security.Cryptography;
using System.Text;

namespace EasyStock.Api.Controllers;

/// <summary>
/// Endpoints de operação SuperAdmin sobre usuários COMUNS dos tenants
/// (não admins do back-office — esses ficam em <see cref="AdminAdminsController"/>).
/// Cobre o fluxo "cliente acionou o suporte e precisa que façamos algo no usuário dele":
/// resetar senha, forçar logout, listar/revogar sessões. Cada ação exige justificativa
/// (motivo) que vai pro AdminAuditLog pra trilha LGPD.
/// </summary>
[ApiController]
[Route("api/admin/usuarios-tenant")]
[Authorize(Policy = "SuperAdmin")]
public class AdminUsuariosTenantController(
    EasyStockDbContext db,
    IRefreshTokenRepository refreshTokens,
    IEmailService emailService,
    AdminAuditService audit,
    CriarUsuarioTenantPorAdminUseCase criarUsuarioUseCase,
    ILogger<AdminUsuariosTenantController> logger) : EasyStockControllerBase
{
    // ─────────────────────────── Criar usuário em tenant existente ───────────────────────────

    /// <summary>
    /// Cria um usuário em um tenant existente. Senha temporária gerada server-side é
    /// retornada 1x (banner do admin) e opcionalmente enviada por email para o novo
    /// usuário. Justificativa (≥10 chars) obrigatória → AdminAuditLog.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Criar([FromBody] CriarUsuarioTenantRequest req)
    {
        if (!RequestGuards.TryValidarMotivo(req?.Motivo, out var motivo, out var erro))
            return DataBadRequest(erro!);
        if (!TryEnsureNotEmpty(req!.TenantId, "Cliente", out var idErro)) return idErro!;
        if (!Enum.TryParse<NivelAcesso>(req.Nivel, ignoreCase: true, out var nivel))
            return DataBadRequest("Nível de acesso inválido. Use Admin, Gerente, Operador ou Visualizador.");

        // Bloqueio: tenant suspenso/cancelado não recebe usuário novo. Mensagem direta
        // pra que o operador entenda que precisa reativar antes.
        var assinatura = await db.AssinaturasEmpresa
            .AsNoTracking()
            .Where(a => a.EmpresaId == req.TenantId)
            .OrderByDescending(a => a.CriadoEm)
            .FirstOrDefaultAsync();
        if (assinatura?.Status == StatusAssinatura.Suspensa)
            return DataBadRequest("Cliente está suspenso. Reative antes de adicionar usuários.");
        if (assinatura?.Status == StatusAssinatura.Cancelada)
            return DataBadRequest("Cliente está cancelado. Não é possível adicionar usuários.");

        try
        {
            var resultado = await criarUsuarioUseCase.ExecuteAsync(new CriarUsuarioTenantPorAdminCommand(
                TenantId: req.TenantId,
                Nome: req.Nome ?? string.Empty,
                Email: req.Email ?? string.Empty,
                Nivel: nivel,
                EnviarEmail: req.EnviarEmail ?? true));

            await audit.LogAsync(
                "AdminCriouUsuarioTenant",
                $"UserId={resultado.UsuarioId}, Email={MascararEmail(resultado.Email)}, Nivel={resultado.Nivel}, EmailEnviado={resultado.EmailEnviado}",
                tenantId: resultado.TenantId,
                motivo: motivo,
                entidadeAfetadaId: resultado.UsuarioId);

            return DataCreated($"/api/admin/usuarios-tenant/{resultado.UsuarioId}", new
            {
                usuarioId = resultado.UsuarioId,
                tenantId = resultado.TenantId,
                nome = resultado.Nome,
                email = resultado.Email,
                nivel = resultado.Nivel.ToString(),
                senhaTemporaria = resultado.SenhaTemporaria,
                emailEnviado = resultado.EmailEnviado,
                emailErro = resultado.EmailErro
            });
        }
        catch (UseCaseValidationException ex) { return DataBadRequest(ex.Message); }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao criar usuário no tenant {TenantId}", req.TenantId);
            return Problem(detail: ex.Message, statusCode: 500, title: "Erro ao criar usuário.");
        }
    }

    // ─────────────────────────── #6: Listar sessões ───────────────────────────

    [HttpGet("{userId:guid}/sessoes")]
    public async Task<IActionResult> ListarSessoes(Guid userId)
    {
        var usuario = await db.Usuarios.FindAsync(userId);
        if (usuario is null) return DataNotFound("Usuário não encontrado.");

        var agora = DateTime.UtcNow;
        var tokens = await refreshTokens.GetByUsuarioIdAsync(userId);

        var sessoes = tokens
            .OrderByDescending(t => t.CriadoEm)
            .Select(t => new
            {
                id = t.Id,
                criadoEm = t.CriadoEm,
                expiraEm = t.ExpiraEm,
                revogado = t.Revogado,
                revogadoEm = t.RevogadoEm,
                ip = MascararIp(t.IpCriacao),
                userAgent = t.UserAgent,
                ativa = !t.Revogado && t.ExpiraEm > agora
            })
            .ToList();

        await audit.LogAsync(
            "UsuarioTenantSessoesVisualizadas",
            $"UserId={userId}, Total={sessoes.Count}",
            tenantId: await ResolverTenantIdAsync(userId),
            entidadeAfetadaId: userId);

        return DataOk(new
        {
            usuarioId = userId,
            usuarioNome = usuario.Nome,
            usuarioEmail = MascararEmail(usuario.Email),
            ativas = sessoes.Count(s => s.ativa),
            total = sessoes.Count,
            sessoes
        });
    }

    // ─────────────────────────── #5: Forçar logout ───────────────────────────

    [HttpPost("{userId:guid}/forcar-logout")]
    public async Task<IActionResult> ForcarLogout(Guid userId, [FromBody] MotivoRequest req)
    {
        if (!RequestGuards.TryValidarMotivo(req?.Motivo, out var motivo, out var erro))
            return DataBadRequest(erro!);

        var usuario = await db.Usuarios.FindAsync(userId);
        if (usuario is null) return DataNotFound("Usuário não encontrado.");

        var revogados = await refreshTokens.RevogarSessoesAtivasAsync(userId, DateTime.UtcNow);

        await audit.LogAsync(
            "UsuarioTenantForcarLogout",
            $"UserId={userId}, Email={MascararEmail(usuario.Email)}, Revogados={revogados}",
            tenantId: await ResolverTenantIdAsync(userId),
            motivo: motivo,
            entidadeAfetadaId: userId);

        logger.LogInformation(
            "AUDIT: Admin forçou logout de usuário {UserId}; {Revogados} sessões revogadas. Motivo: {Motivo}",
            userId, revogados, motivo);

        return DataOk(new
        {
            usuarioId = userId,
            sessoesRevogadas = revogados,
            mensagem = revogados > 0
                ? $"{revogados} sessão(ões) ativa(s) revogada(s)."
                : "Nenhuma sessão ativa encontrada — usuário já estava deslogado."
        });
    }

    // ─────────────────────────── #1: Reset de senha ───────────────────────────

    [HttpPost("{userId:guid}/reset-senha")]
    public async Task<IActionResult> ResetarSenha(Guid userId, [FromBody] ResetSenhaRequest req)
    {
        if (!RequestGuards.TryValidarMotivo(req?.Motivo, out var motivo, out var erro))
            return DataBadRequest(erro!);

        var usuario = await db.Usuarios.FindAsync(userId);
        if (usuario is null) return DataNotFound("Usuário não encontrado.");
        if (!usuario.Ativo) return DataBadRequest("Usuário está inativo. Reative-o antes de resetar a senha.");

        // Senha temporária forte (RandomNumberGenerator.GetInt32 evita modulo bias).
        var novaSenha = GerarSenhaAleatoria(16);
        usuario.SenhaHash = BCrypt.Net.BCrypt.HashPassword(novaSenha);
        usuario.AlteradoEm = DateTime.UtcNow;
        usuario.ResetarTentativasFalha();
        await db.CommitAsync();

        // Revoga sessões ativas — senha trocada implica invalidar quem está logado
        // com a senha antiga. Padrão Auth0/Stripe.
        var sessoesRevogadas = await refreshTokens.RevogarSessoesAtivasAsync(userId, DateTime.UtcNow);

        await audit.LogAsync(
            "UsuarioTenantSenhaReset",
            $"UserId={userId}, Email={MascararEmail(usuario.Email)}, SessoesRevogadas={sessoesRevogadas}",
            tenantId: await ResolverTenantIdAsync(userId),
            motivo: motivo,
            entidadeAfetadaId: userId);

        // Envio por email — NUNCA retornar plaintext na response (vaza em logs/history).
        var emailEnviado = false;
        var emailErro = (string?)null;
        if (req?.EnviarPorEmail ?? true)
        {
            try
            {
                var body = $@"<p>Olá {System.Net.WebUtility.HtmlEncode(usuario.Nome)},</p>
<p>Sua senha do EasyStock foi resetada pelo time de suporte.</p>
<p><strong>Nova senha temporária:</strong> <code>{System.Net.WebUtility.HtmlEncode(novaSenha)}</code></p>
<p>Recomendamos trocar essa senha após o primeiro login.</p>
<p>Se você não solicitou esta alteração, contate-nos imediatamente.</p>";
                await emailService.SendAsync(usuario.Email, "EasyStock — Sua senha foi resetada", body, isHtml: true);
                emailEnviado = true;
            }
            catch (Exception ex)
            {
                emailErro = ex.Message;
                logger.LogWarning(ex, "Falha ao enviar email de reset de senha para usuário {UserId}", userId);
            }
        }

        return DataOk(new
        {
            usuarioId = userId,
            sessoesRevogadas,
            emailEnviado,
            emailErro,
            mensagem = emailEnviado
                ? "Senha resetada e email enviado para o usuário."
                : "Senha resetada — falha no envio de email; informe o usuário pelo canal oficial."
        });
    }

    // ─────────────────────────── #2: Editar nome/email ───────────────────────────

    [HttpPatch("{userId:guid}")]
    public async Task<IActionResult> Atualizar(Guid userId, [FromBody] AtualizarUsuarioRequest req)
    {
        if (!RequestGuards.TryValidarMotivo(req?.Motivo, out var motivo, out var erro))
            return DataBadRequest(erro!);

        var nomeNovo = req?.Nome?.Trim();
        var emailNovo = req?.Email?.Trim().ToLowerInvariant();

        // Pelo menos um campo precisa estar presente.
        if (string.IsNullOrWhiteSpace(nomeNovo) && string.IsNullOrWhiteSpace(emailNovo))
            return DataBadRequest("Informe ao menos um campo (nome ou email) para atualizar.");

        if (!string.IsNullOrWhiteSpace(nomeNovo) && (nomeNovo.Length is < 2 or > 120))
            return DataBadRequest("Nome deve ter entre 2 e 120 caracteres.");

        if (!string.IsNullOrWhiteSpace(emailNovo))
        {
            if (emailNovo.Length > 160 || !System.Text.RegularExpressions.Regex.IsMatch(emailNovo, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
                return DataBadRequest("Email inválido.");
        }

        var usuario = await db.Usuarios.FindAsync(userId);
        if (usuario is null) return DataNotFound("Usuário não encontrado.");

        // Diff antes/depois — registra explicitamente no audit log pra forensics.
        var nomeAntigo = usuario.Nome;
        var emailAntigo = usuario.Email;
        var alteracoes = new List<string>();

        if (!string.IsNullOrWhiteSpace(nomeNovo) && !string.Equals(nomeNovo, nomeAntigo, StringComparison.Ordinal))
        {
            alteracoes.Add($"nome: '{nomeAntigo}' → '{nomeNovo}'");
            usuario.Nome = nomeNovo;
        }

        var emailTrocado = false;
        if (!string.IsNullOrWhiteSpace(emailNovo) && !string.Equals(emailNovo, emailAntigo, StringComparison.OrdinalIgnoreCase))
        {
            // Conflito: outro usuário já usa esse email.
            var ocupado = await db.Usuarios.AnyAsync(u => u.Email == emailNovo && u.Id != userId);
            if (ocupado)
                return Conflict(new { error = new { code = "EMAIL_DUPLICADO", message = "Este e-mail já está em uso por outro usuário." } });
            alteracoes.Add($"email: '{MascararEmail(emailAntigo)}' → '{MascararEmail(emailNovo)}'");
            usuario.Email = emailNovo;
            usuario.EmailConfirmado = false; // Forçar reconfirmação na próxima ação que exigir.
            emailTrocado = true;
        }

        if (alteracoes.Count == 0)
            return DataOk(new { usuarioId = userId, alterado = false, mensagem = "Nenhuma alteração — valores idênticos aos atuais." });

        usuario.AlteradoEm = DateTime.UtcNow;
        await db.CommitAsync();

        // Email troca → revoga refresh tokens. Defesa contra atacante que captura sessão
        // e troca o email de identidade. Padrão Auth0: "credential change → invalidate sessions".
        var sessoesRevogadas = 0;
        if (emailTrocado)
        {
            sessoesRevogadas = await refreshTokens.RevogarSessoesAtivasAsync(userId, DateTime.UtcNow);
        }

        await audit.LogAsync(
            "UsuarioTenantAtualizado",
            $"UserId={userId}; {string.Join("; ", alteracoes)}{(emailTrocado ? $"; SessoesRevogadas={sessoesRevogadas}" : "")}",
            tenantId: await ResolverTenantIdAsync(userId),
            motivo: motivo,
            entidadeAfetadaId: userId);

        logger.LogInformation(
            "AUDIT: Admin atualizou usuário {UserId}. Mudanças: {Alteracoes}. Motivo: {Motivo}",
            userId, string.Join("; ", alteracoes), motivo);

        return DataOk(new
        {
            usuarioId = userId,
            alterado = true,
            alteracoes,
            sessoesRevogadas = emailTrocado ? (int?)sessoesRevogadas : null,
            mensagem = $"{alteracoes.Count} campo(s) atualizado(s)."
                + (emailTrocado ? $" Email trocado → {sessoesRevogadas} sessão(ões) revogada(s)." : "")
        });
    }

    // ─────────────────────── #3 / #4: Desativar / Reativar ───────────────────────

    [HttpPost("{userId:guid}/desativar")]
    public async Task<IActionResult> Desativar(Guid userId, [FromBody] MotivoRequest req)
    {
        if (!RequestGuards.TryValidarMotivo(req?.Motivo, out var motivo, out var erro))
            return DataBadRequest(erro!);

        var usuario = await db.Usuarios.FindAsync(userId);
        if (usuario is null) return DataNotFound("Usuário não encontrado.");
        if (!usuario.Ativo) return DataBadRequest("Usuário já está inativo.");

        // Guard: nunca deixar tenant sem nenhum admin ativo. Se o usuário a desativar é
        // o único admin do tenant, recusa — operador precisa criar outro admin antes.
        if (await EhUltimoAdminAtivoAsync(userId))
            return DataBadRequest("Este é o último Admin ativo deste cliente. Promova outro usuário antes de desativar este.");

        usuario.Ativo = false;
        usuario.AlteradoEm = DateTime.UtcNow;
        await db.CommitAsync();

        // Desativar implica logout — refresh tokens não devem mais funcionar.
        var sessoesRevogadas = await refreshTokens.RevogarSessoesAtivasAsync(userId, DateTime.UtcNow);

        await audit.LogAsync(
            "UsuarioTenantDesativado",
            $"UserId={userId}, Email={MascararEmail(usuario.Email)}, SessoesRevogadas={sessoesRevogadas}",
            tenantId: await ResolverTenantIdAsync(userId),
            motivo: motivo,
            entidadeAfetadaId: userId);

        return DataOk(new { usuarioId = userId, ativo = false, sessoesRevogadas });
    }

    [HttpPost("{userId:guid}/reativar")]
    public async Task<IActionResult> Reativar(Guid userId, [FromBody] MotivoRequest req)
    {
        if (!RequestGuards.TryValidarMotivo(req?.Motivo, out var motivo, out var erro))
            return DataBadRequest(erro!);

        var usuario = await db.Usuarios.FindAsync(userId);
        if (usuario is null) return DataNotFound("Usuário não encontrado.");
        if (usuario.Ativo) return DataBadRequest("Usuário já está ativo.");

        usuario.Ativo = true;
        usuario.ResetarTentativasFalha();
        usuario.AlteradoEm = DateTime.UtcNow;
        await db.CommitAsync();

        await audit.LogAsync(
            "UsuarioTenantReativado",
            $"UserId={userId}, Email={MascararEmail(usuario.Email)}",
            tenantId: await ResolverTenantIdAsync(userId),
            motivo: motivo,
            entidadeAfetadaId: userId);

        return DataOk(new { usuarioId = userId, ativo = true });
    }

    // ─────────────────────────── helpers ───────────────────────────

    /// <summary>
    /// True se o usuário-alvo é admin (Admin ou SuperAdmin) de algum tenant E é o
    /// único admin ativo desse tenant. Evita deixar cliente sem dono. Bug histórico:
    /// versão anterior bloqueava desativar QUALQUER usuário (mesmo não-admin) sempre
    /// que o tenant tinha 1 só admin — agora valida especificamente que o alvo é admin.
    /// </summary>
    private async Task<bool> EhUltimoAdminAtivoAsync(Guid usuarioId)
    {
        // 1. Empresas onde o usuário-alvo é admin ATIVO.
        var empresasOndeEhAdmin = await db.UsuariosPerfis
            .Where(up => up.UsuarioId == usuarioId)
            .Join(db.Perfis, up => up.PerfilId, p => p.Id, (up, p) => new { up.EmpresaId, p.Nivel })
            .Where(x => x.Nivel == Domain.Enums.NivelAcesso.Admin || x.Nivel == Domain.Enums.NivelAcesso.SuperAdmin)
            .Select(x => x.EmpresaId)
            .Distinct()
            .ToListAsync();

        if (empresasOndeEhAdmin.Count == 0) return false; // alvo não é admin em lugar nenhum.

        foreach (var empresaId in empresasOndeEhAdmin)
        {
            // 2. Conta admins ativos DIFERENTES do alvo nessa empresa.
            var outrosAdminsAtivos = await db.UsuariosPerfis
                .Where(up => up.EmpresaId == empresaId && up.UsuarioId != usuarioId)
                .Join(db.Perfis, up => up.PerfilId, p => p.Id, (up, p) => new { up.UsuarioId, p.Nivel })
                .Where(x => x.Nivel == Domain.Enums.NivelAcesso.Admin || x.Nivel == Domain.Enums.NivelAcesso.SuperAdmin)
                .Join(db.Usuarios, x => x.UsuarioId, u => u.Id, (x, u) => new { x.UsuarioId, u.Ativo })
                .Where(x => x.Ativo)
                .Select(x => x.UsuarioId)
                .Distinct()
                .CountAsync();

            // Se não sobra nenhum outro admin ativo, o alvo é o último — bloqueia.
            if (outrosAdminsAtivos == 0) return true;
        }
        return false;
    }



    /// <summary>
    /// Resolve EmpresaId do usuário (primeira empresa vinculada). Null = sem tenant.
    /// Query separada porque Usuarios.FindAsync não eager-loada Empresas.
    /// </summary>
    private async Task<Guid?> ResolverTenantIdAsync(Guid usuarioId)
    {
        return await db.UsuariosEmpresas
            .Where(ue => ue.UsuarioId == usuarioId)
            .Select(ue => (Guid?)ue.EmpresaId)
            .FirstOrDefaultAsync();
    }

    private static string MascararEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "(vazio)";
        var at = email.IndexOf('@');
        if (at <= 0 || at == email.Length - 1) return "***";
        var dominio = email[(at + 1)..];
        return email[0] + "***@" + dominio;
    }

    private static string? MascararIp(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return null;
        var partes = ip.Split('.');
        if (partes.Length == 4) return $"{partes[0]}.{partes[1]}.*.*";
        // IPv6 ou formato desconhecido: mascara últimos 8 chars.
        return ip.Length > 8 ? string.Concat(ip.AsSpan(0, ip.Length - 8), "********") : "***";
    }

    private static string GerarSenhaAleatoria(int length)
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789!@#$";
        var sb = new StringBuilder(length);
        for (var i = 0; i < length; i++)
            sb.Append(chars[RandomNumberGenerator.GetInt32(chars.Length)]);
        return sb.ToString();
    }
}

public record MotivoRequest(string Motivo);
public record ResetSenhaRequest(string Motivo, bool? EnviarPorEmail);
public record AtualizarUsuarioRequest(string Motivo, string? Nome, string? Email);
public record CriarUsuarioTenantRequest(
    string Motivo,
    Guid TenantId,
    string? Nome,
    string? Email,
    string Nivel,
    bool? EnviarEmail);

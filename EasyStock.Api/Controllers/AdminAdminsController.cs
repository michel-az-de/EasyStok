using EasyStock.Api.Http;
using EasyStock.Api.Services;
using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace EasyStock.Api.Controllers;

[ApiController]
[Route("api/admin/admins")]
[Authorize(Policy = "SuperAdmin")]
public class AdminAdminsController(
    EasyStockDbContext db,
    ICurrentUserAccessor currentUser,
    AdminAuditService audit,
    EasyStock.Application.Ports.Output.IEmailService emailService,
    ILogger<AdminAdminsController> logger) : EasyStockControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAdmins()
    {
        var superAdminPerfis = await db.Perfis
            .Where(p => p.Nivel == NivelAcesso.SuperAdmin)
            .Select(p => p.Id)
            .ToListAsync();

        var admins = await db.UsuariosPerfis
            .Where(up => superAdminPerfis.Contains(up.PerfilId))
            .Select(up => up.UsuarioId)
            .Distinct()
            .Join(db.Usuarios, id => id, u => u.Id, (_, u) => new
            {
                u.Id,
                u.Nome,
                u.Email,
                u.Ativo,
                u.UltimoAcessoEm,
                u.CriadoEm
            })
            .OrderBy(u => u.Nome)
            .ToListAsync();

        return DataOk(admins);
    }

    [HttpPost]
    public async Task<IActionResult> CreateAdmin([FromBody] CreateAdminRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Nome) || string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Senha))
            return DataBadRequest("Nome, Email e Senha são obrigatórios.");

        if (await db.Usuarios.AnyAsync(u => u.Email == req.Email.ToLowerInvariant()))
            return Conflict(new { error = new { code = "EMAIL_JA_EXISTE", message = "Já existe um usuário com este e-mail." } });

        // Find or create the SuperAdmin perfil
        var perfilSuperAdmin = await db.Perfis.FirstOrDefaultAsync(p => p.Nivel == NivelAcesso.SuperAdmin && p.EmpresaId == null);
        if (perfilSuperAdmin is null)
        {
            perfilSuperAdmin = new Perfil
            {
                Id = Guid.NewGuid(),
                Nome = "SuperAdmin",
                Descricao = "Administrador do sistema",
                Nivel = NivelAcesso.SuperAdmin,
                EmpresaId = null,
                CriadoEm = DateTime.UtcNow
            };
            db.Perfis.Add(perfilSuperAdmin);
        }

        var senhaHash = BCrypt.Net.BCrypt.HashPassword(req.Senha);
        var usuario = Usuario.Criar(req.Nome.Trim(), req.Email.ToLowerInvariant().Trim(), senhaHash);
        db.Usuarios.Add(usuario);

        db.UsuariosPerfis.Add(new UsuarioPerfil
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuario.Id,
            PerfilId = perfilSuperAdmin.Id,
            EmpresaId = Guid.Empty,
            AtribuidoEm = DateTime.UtcNow,
            AtribuidoPorId = currentUser.UsuarioId
        });

        await db.CommitAsync();
        await audit.LogAsync("AdminCriado", $"Email={usuario.Email}");

        return DataCreated($"api/admin/admins/{usuario.Id}", new { usuario.Id, usuario.Nome, usuario.Email, usuario.Ativo });
    }

    [HttpPatch("{id:guid}/toggle")]
    public async Task<IActionResult> Toggle(Guid id)
    {
        var usuario = await db.Usuarios.FindAsync(id);
        if (usuario is null) return DataNotFound("Admin não encontrado.");

        // Guard: cannot deactivate yourself
        if (id == currentUser.UsuarioId)
            return DataBadRequest("Você não pode desativar sua própria conta.");

        usuario.Ativo = !usuario.Ativo;
        usuario.AlteradoEm = DateTime.UtcNow;
        await db.CommitAsync();
        await audit.LogAsync("AdminToggle", $"AdminId={id}, Ativo={usuario.Ativo}");

        return DataOk(new { id, ativo = usuario.Ativo });
    }

    [HttpPost("{id:guid}/reset-senha")]
    public async Task<IActionResult> ResetSenha(Guid id)
    {
        var usuario = await db.Usuarios.FindAsync(id);
        if (usuario is null) return DataNotFound("Admin não encontrado.");

        var novaSenha = GerarSenhaAleatoria(16);
        usuario.SenhaHash = BCrypt.Net.BCrypt.HashPassword(novaSenha);
        usuario.AlteradoEm = DateTime.UtcNow;
        await db.CommitAsync();
        await audit.LogAsync("AdminSenhaReset", $"AdminId={id}");

        // Enviar por email — NUNCA retornar plaintext na response (vaza em logs/history).
        try
        {
            var body = $@"<p>Sua senha do painel admin EasyStock foi resetada.</p>
<p><strong>Nova senha:</strong> <code>{System.Net.WebUtility.HtmlEncode(novaSenha)}</code></p>
<p>Recomendamos trocar essa senha após o primeiro login.</p>";
            await emailService.SendAsync(usuario.Email, "EasyStock Admin — Senha resetada", body, isHtml: true);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao enviar email de reset de senha para admin {Id}", id);
        }

        return DataOk(new { id, mensagem = "Senha resetada. Nova senha enviada por email." });
    }

    private static string GerarSenhaAleatoria(int length)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$";
        // RandomNumberGenerator.GetInt32 elimina modulo bias automaticamente
        // (256 % 65 != 0 → bytes brutos enviesariam chars iniciais).
        var sb = new StringBuilder(length);
        for (var i = 0; i < length; i++)
            sb.Append(chars[RandomNumberGenerator.GetInt32(chars.Length)]);
        return sb.ToString();
    }
}

public record CreateAdminRequest(string Nome, string Email, string Senha);

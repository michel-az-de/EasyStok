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
    AdminAuditService audit) : EasyStockControllerBase
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

        var novaSenha = GerarSenhaAleatoria(12);
        usuario.SenhaHash = BCrypt.Net.BCrypt.HashPassword(novaSenha);
        usuario.AlteradoEm = DateTime.UtcNow;
        await db.CommitAsync();
        await audit.LogAsync("AdminSenhaReset", $"AdminId={id}");

        return DataOk(new { novaSenha });
    }

    private static string GerarSenhaAleatoria(int length)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$";
        var bytes = RandomNumberGenerator.GetBytes(length);
        var sb = new StringBuilder(length);
        foreach (var b in bytes)
            sb.Append(chars[b % chars.Length]);
        return sb.ToString();
    }
}

public record CreateAdminRequest(string Nome, string Email, string Senha);

using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.Data;

/// <summary>
/// Garante que existe ao menos um SuperAdmin global (Perfil.EmpresaId=null) no
/// banco para acesso ao painel /EasyStock.Admin. Nenhum dos seeds de tenant
/// (PastaBella, CasaDaBaba, etc.) cria SuperAdmin — eles seedam apenas Admin
/// de empresa. Sem este bootstrap, o painel admin fica inacessivel apos um
/// reset de banco em ambientes onde o usuario foi criado manualmente.
///
/// Idempotente: roda sempre no startup, no-op se ja existe.
/// Lê env vars: SEED_SUPERADMIN_EMAIL e SEED_SUPERADMIN_PASSWORD (defaults
/// abaixo). Em Render/produtivo, configurar as vars com valores fortes.
/// </summary>
public static class SuperAdminSeed
{
    private const string DefaultEmail = "admin@easystok.com";
    private const string DefaultPassword = "Admin@2026!Secure";
    private const string DefaultNome = "Super Admin";

    public static async Task ExecutarAsync(EasyStockDbContext context, ILogger logger)
    {
        var email = (Environment.GetEnvironmentVariable("SEED_SUPERADMIN_EMAIL") ?? DefaultEmail)
            .Trim().ToLowerInvariant();
        var senha = Environment.GetEnvironmentVariable("SEED_SUPERADMIN_PASSWORD") ?? DefaultPassword;
        var nome = Environment.GetEnvironmentVariable("SEED_SUPERADMIN_NOME") ?? DefaultNome;

        var agora = DateTime.UtcNow;

        // IgnoreQueryFilters em TODAS as queries — Perfil tem EmpresaId Guid? e o
        // global query filter elimina EmpresaId=null quando CurrentTenantId=Guid.Empty
        // (sempre durante startup). Sem isso o seed sempre acharia "nao existe" e
        // tentaria duplicar.
        var perfilSuper = await context.Perfis
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Nivel == NivelAcesso.SuperAdmin && p.EmpresaId == null);

        if (perfilSuper is null)
        {
            perfilSuper = new Perfil
            {
                Id = Guid.NewGuid(),
                Nome = "SuperAdmin",
                Descricao = "Administrador global do sistema (cross-tenant)",
                Nivel = NivelAcesso.SuperAdmin,
                EmpresaId = null,
                CriadoEm = agora
            };
            context.Perfis.Add(perfilSuper);
            logger.LogInformation("[SuperAdminSeed] Perfil global SuperAdmin criado (Id={Id}).", perfilSuper.Id);
        }

        var usuario = await context.Usuarios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == email);

        if (usuario is null)
        {
            usuario = Usuario.Criar(nome, email, BCrypt.Net.BCrypt.HashPassword(senha));
            usuario.EmailConfirmado = true;
            usuario.Ativo = true;
            context.Usuarios.Add(usuario);
            logger.LogInformation("[SuperAdminSeed] Usuario SuperAdmin criado: {Email}.", email);
        }
        else
        {
            // Bootstrap defensivo: se o usuario existe mas esta inativo/nao confirmado,
            // restaura. NAO sobrescreve a senha em runs subsequentes — primeiro acesso
            // com a senha default, depois Felipe troca via /Auth/me/password.
            var mudou = false;
            if (!usuario.Ativo) { usuario.Ativo = true; mudou = true; }
            if (!usuario.EmailConfirmado) { usuario.EmailConfirmado = true; mudou = true; }
            if (mudou)
            {
                usuario.AlteradoEm = agora;
                logger.LogInformation("[SuperAdminSeed] Usuario {Email} reativado/confirmado.", email);
            }
        }

        // SaveChanges intermediario para garantir Ids antes do UsuarioPerfil
        await context.SaveChangesAsync();

        var vinculo = await context.UsuariosPerfis
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(up => up.UsuarioId == usuario.Id && up.PerfilId == perfilSuper.Id);

        if (vinculo is null)
        {
            context.UsuariosPerfis.Add(new UsuarioPerfil
            {
                Id = Guid.NewGuid(),
                UsuarioId = usuario.Id,
                PerfilId = perfilSuper.Id,
                EmpresaId = Guid.Empty,
                AtribuidoEm = agora,
                AtribuidoPorId = null
            });
            await context.SaveChangesAsync();
            logger.LogInformation("[SuperAdminSeed] Vinculo Usuario↔PerfilSuperAdmin criado.");
        }
    }
}

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
///
/// R6: em Production, SEED_SUPERADMIN_EMAIL e SEED_SUPERADMIN_PASSWORD sao
/// OBRIGATORIAS (env vars). Senha hardcoded foi removida — passar isProduction=true
/// causa fail-fast se ausentes ou se senha for fraca/conhecida.
/// </summary>
public static class SuperAdminSeed
{
    private const string DefaultEmail = "admin@easystok.com";
    private const string DefaultNome = "Super Admin";
    // Apenas usado em Development. Em Production e fail-fast se SEED_SUPERADMIN_PASSWORD ausente.
    private const string DevOnlyDefaultPassword = "Admin@2026!Secure";

    /// <summary>
    /// Senhas conhecidas/fracas/leaked que nao podem ser aceitas em Production
    /// mesmo se setadas explicitamente em SEED_SUPERADMIN_PASSWORD.
    /// </summary>
    private static readonly HashSet<string> SenhasProibidasEmProducao = new(StringComparer.Ordinal)
    {
        "Admin@2026!Secure",
        "admin@2026!secure",
        "Admin@123",
        "admin@123",
        "admin123",
        "Admin123",
        "password",
        "Password123",
        "P@ssw0rd",
        "12345678",
        "qwerty123",
        "easystock",
        "EasyStock"
    };

    public static async Task ExecutarAsync(EasyStockDbContext context, ILogger logger, bool isProduction = false)
    {
        var email = (Environment.GetEnvironmentVariable("SEED_SUPERADMIN_EMAIL") ?? DefaultEmail)
            .Trim().ToLowerInvariant();
        var senhaEnvRaw = Environment.GetEnvironmentVariable("SEED_SUPERADMIN_PASSWORD");
        var nome = Environment.GetEnvironmentVariable("SEED_SUPERADMIN_NOME") ?? DefaultNome;

        // R6 — fail-fast em Production: exige email/senha em env vars + senha forte.
        // Nao subir API com SuperAdmin acessivel via senha hardcoded conhecida.
        if (isProduction)
        {
            if (string.IsNullOrWhiteSpace(senhaEnvRaw))
            {
                throw new InvalidOperationException(
                    "[SuperAdminSeed][PROD] SEED_SUPERADMIN_PASSWORD e obrigatoria em Production. " +
                    "Defina como env var no Azure App Service Settings (>= 12 chars, sem palavras conhecidas). " +
                    "Sem isso, o bootstrap de SuperAdmin nao roda e o painel /EasyStock.Admin fica inacessivel.");
            }
            if (Environment.GetEnvironmentVariable("SEED_SUPERADMIN_EMAIL") is null or "")
            {
                throw new InvalidOperationException(
                    "[SuperAdminSeed][PROD] SEED_SUPERADMIN_EMAIL e obrigatoria em Production.");
            }
            if (senhaEnvRaw.Length < 12)
            {
                throw new InvalidOperationException(
                    "[SuperAdminSeed][PROD] SEED_SUPERADMIN_PASSWORD deve ter no minimo 12 caracteres.");
            }
            if (SenhasProibidasEmProducao.Contains(senhaEnvRaw))
            {
                throw new InvalidOperationException(
                    "[SuperAdminSeed][PROD] SEED_SUPERADMIN_PASSWORD esta na lista de senhas conhecidas/fracas — rejeitada. Gere uma senha forte unica.");
            }
        }

        var senha = senhaEnvRaw ?? DevOnlyDefaultPassword;
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

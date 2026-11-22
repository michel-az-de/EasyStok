using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Security;
using EasyStock.Infra.Postgre.Data;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Infra.Postgre.Notifications.Audiencia;

/// <summary>
/// Implementa <see cref="ISuperAdminsDaPlataforma"/> (N4, #1373): lê quem tem o perfil de suporte global (como o login:
/// <c>Perfil.SuporteGlobal</c>, nível SuperAdmin e sem empresa, B1 de #2070). O SuperAdmin é global (<c>Perfil.EmpresaId</c>
/// nulo, <c>UsuarioPerfil.EmpresaId = Guid.Empty</c>, sem <c>UsuarioEmpresa</c>), então nenhuma consulta por empresa o
/// acha: a leitura é cross-tenant por natureza. Roda num escopo de DI próprio e curto, com o bypass de RLS aberto pela
/// porta <see cref="IRowLevelSecurityBypass"/> antes da primeira conexão (o interceptor só lê a flag na abertura), e
/// nunca dentro do escopo do evento. Só leitura, só Id, nome e contato.
/// </summary>
public sealed class SuperAdminsDaPlataformaQuery(IServiceScopeFactory scopeFactory) : ISuperAdminsDaPlataforma
{
    public async Task<IReadOnlyList<UsuarioParaAudiencia>> ListarAsync(CancellationToken ct = default)
    {
        using var escopo = scopeFactory.CreateScope();
        var sp = escopo.ServiceProvider;

        // O bypass é do DbContext do escopo: ligar antes de qualquer consulta, e só dentro deste escopo.
        using var _ = sp.GetRequiredService<IRowLevelSecurityBypass>().Begin();
        var db = sp.GetRequiredService<EasyStockDbContext>();

        // B1 (#2070): só o perfil global de suporte (Perfil.SuporteGlobal), nunca um perfil de empresa com nível SuperAdmin.
        var perfisDeSuporte = db.Perfis.IgnoreQueryFilters().Where(Perfil.SuporteGlobal).Select(p => p.Id);
        var linhas = await db.Usuarios
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(u => u.Ativo
                        && u.Perfis.Any(up => perfisDeSuporte.Contains(up.PerfilId)))
            .OrderBy(u => u.Nome)
            .Select(u => new { u.Id, u.Nome, u.Email, u.EmailConfirmado, u.Ativo, u.Telefone, u.TelefoneVerificadoEm })
            .ToListAsync(ct);

        return linhas
            .Select(u => new UsuarioParaAudiencia(
                u.Id, u.Nome, u.Email, u.EmailConfirmado, u.Ativo, u.Telefone?.Value, u.TelefoneVerificadoEm))
            .ToList();
    }
}

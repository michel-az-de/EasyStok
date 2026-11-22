using EasyStock.Domain.Entities.Mobile;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Security;

namespace EasyStock.Api.Mobile.Security;

/// <summary>
/// #2180: a policy das mobile_* segue o tenant da conexão, não o nível do usuário. O SuperAdmin já
/// autorizado a operar um aparelho de outra empresa aponta a requisição para a empresa do aparelho:
/// o bypass cobre só a leitura de qual empresa é, e o resto da requisição segue isolado nela.
/// </summary>
public static class EmpresaDoSuperAdmin
{
    public static async Task UsarEmpresaDoAparelhoAsync(this EasyStockDbContext db, string deviceId, CancellationToken ct)
    {
        Guid? empresa;
        await using (await db.UseRowLevelSecurityBypassAsync(ct))
            empresa = await db.Set<MobileDevice>().AsNoTracking()
                .Where(d => d.Id == deviceId)
                .Select(d => (Guid?)d.EmpresaId)
                .FirstOrDefaultAsync(ct);
        if (empresa is { } id) db.SetMobileTenantContext(id);
    }
}

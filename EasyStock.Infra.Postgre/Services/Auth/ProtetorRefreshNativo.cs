using EasyStock.Application.Ports.Output.Auth;
using Microsoft.AspNetCore.DataProtection;

namespace EasyStock.Infra.Postgre.Services.Auth;

public sealed class ProtetorRefreshNativo(IDataProtectionProvider provider) : IProtetorRefreshNativo
{
    private IDataProtector Protetor(Guid sessaoId) => provider.CreateProtector("EasyStok.RefreshNativo.v1", sessaoId.ToString("N"));
    public string Proteger(Guid sessaoId, string refreshToken) => Protetor(sessaoId).Protect(refreshToken);
    public string Desproteger(Guid sessaoId, string protegido) => Protetor(sessaoId).Unprotect(protegido);
}

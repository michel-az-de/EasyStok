using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Integration.Crypto;
using EasyStock.Domain.Integration;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Services.Atendimento;

/// <summary>
/// Lê <see cref="Empresa.WhatsAppPhoneNumberId"/> da empresa do tenant corrente
/// (<see cref="EasyStockDbContext.CurrentTenantId"/>: JWT no console, override no webhook e nos
/// jobs). Scoped como o DbContext; o valor fica em memória pelo resto do escopo porque uma
/// resposta pode disparar vários POSTs (texto, botões, marcar como lida). O business token da
/// coexistência (#1417) segue a mesma regra, lido pelo resolver de credenciais cifradas.
/// </summary>
public sealed class RemetenteWhatsAppDoTenant(EasyStockDbContext dbContext, IIntegrationCredentialResolver credenciais)
    : IRemetenteWhatsApp
{
    private Guid? _tenantResolvido;
    private string? _phoneNumberId;
    private Guid? _tenantDoToken;
    private string? _accessToken;

    public bool HaTenantCorrente => dbContext.CurrentTenantId != Guid.Empty;

    public async Task<string?> ObterPhoneNumberIdAsync(CancellationToken ct = default)
    {
        var tenant = dbContext.CurrentTenantId;
        if (tenant == Guid.Empty)
            return null;

        if (_tenantResolvido == tenant)
            return _phoneNumberId;

        _phoneNumberId = await dbContext.Empresas.AsNoTracking()
            .Where(e => e.Id == tenant)
            .Select(e => e.WhatsAppPhoneNumberId)
            .FirstOrDefaultAsync(ct);
        _tenantResolvido = tenant;
        return _phoneNumberId;
    }

    public async Task<string?> ObterAccessTokenAsync(CancellationToken ct = default)
    {
        var tenant = dbContext.CurrentTenantId;
        if (tenant == Guid.Empty)
            return null;

        if (_tenantDoToken == tenant)
            return _accessToken;

        var credencial = await credenciais.ObterAsync<CredencialWhatsAppMeta>(
            tenant, CredencialWhatsAppMeta.ProviderKey, AmbienteIntegracao.Production, ct);
        _accessToken = string.IsNullOrWhiteSpace(credencial?.AccessToken) ? null : credencial.AccessToken.Trim();
        _tenantDoToken = tenant;
        return _accessToken;
    }
}

using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Services.Atendimento;

/// <summary>
/// Lê <see cref="Empresa.WhatsAppPhoneNumberId"/> da empresa do tenant corrente
/// (<see cref="EasyStockDbContext.CurrentTenantId"/>: JWT no console, override no webhook e nos
/// jobs). Scoped como o DbContext; o valor fica em memória pelo resto do escopo porque uma
/// resposta pode disparar vários POSTs (texto, botões, marcar como lida).
/// </summary>
public sealed class RemetenteWhatsAppDoTenant(EasyStockDbContext dbContext) : IRemetenteWhatsApp
{
    private Guid? _tenantResolvido;
    private string? _phoneNumberId;

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
}

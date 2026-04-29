using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Services;

/// <summary>
/// Cliente HTTP pra <c>/api/mobile/devices/*</c> (Onda 1).
/// Usado pela página <c>/dispositivos</c> pra listar pareamentos, gerar
/// códigos e revogar dispositivos da empresa atual.
/// </summary>
public class MobileDevicesService(ApiClient api, SessionService session)
{
    private Guid GetEmpresaId() =>
        Guid.TryParse(session.GetEmpresaId(), out var id) ? id : Guid.Empty;

    private Guid GetLojaId() =>
        Guid.TryParse(session.GetLojaId(), out var id) ? id : Guid.Empty;

    public Task<ApiResult<List<MobileDeviceApi>>> ListarAsync() =>
        api.GetAsync<List<MobileDeviceApi>>($"mobile/devices?empresaId={GetEmpresaId()}");

    public Task<ApiResult<MobilePairCodeApi>> GerarCodigoAsync(string? label, string? defaultOperatorName) =>
        api.PostAsync<MobilePairCodeApi>("mobile/devices/pair-codes", new
        {
            empresaId = GetEmpresaId(),
            lojaId = GetLojaId(),
            deviceId = (string?)null,
            label,
            defaultOperatorName
        });

    public Task<ApiResult<bool>> RevogarAsync(string deviceId) =>
        api.DeleteAsync($"mobile/devices/{deviceId}");
}

/// <summary>Resposta do GET /api/mobile/devices.</summary>
public class MobileDeviceApi
{
    public string Id { get; set; } = "";
    public string? Label { get; set; }
    public Guid EmpresaId { get; set; }
    public Guid LojaId { get; set; }
    public string? DefaultOperatorName { get; set; }
    public DateTime? PairedAt { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public string? LastSeenIp { get; set; }
    public bool Revoked { get; set; }
    public DateTime? RevokedAt { get; set; }
    public bool PendingPair { get; set; }
}

/// <summary>Resposta do POST /api/mobile/devices/pair-codes.</summary>
public class MobilePairCodeApi
{
    public string PairingCode { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public string DeviceRecordId { get; set; } = "";
}

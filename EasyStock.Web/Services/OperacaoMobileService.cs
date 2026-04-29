using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Services;

/// <summary>
/// Onda 4 — Cliente HTTP pra /api/mobile/operation/* + /devices/{id}/commands.
/// Usado pelo painel /operacao do Web.
/// </summary>
public class OperacaoMobileService(ApiClient api, SessionService session)
{
    private Guid GetEmpresaId() =>
        Guid.TryParse(session.GetEmpresaId(), out var id) ? id : Guid.Empty;

    private Guid? GetLojaIdOrNull()
    {
        var s = session.GetLojaId();
        return Guid.TryParse(s, out var id) && id != Guid.Empty ? id : null;
    }

    public Task<ApiResult<OperationDashboardApi>> ObterDashboardAsync()
    {
        var loja = GetLojaIdOrNull();
        var url = $"mobile/operation/dashboard?empresaId={GetEmpresaId()}";
        if (loja.HasValue) url += $"&lojaId={loja}";
        return api.GetAsync<OperationDashboardApi>(url);
    }

    public Task<ApiResult<object>> EnfileirarComandoAsync(string deviceId, string commandType, string? payloadJson = null) =>
        api.PostAsync<object>($"mobile/devices/{deviceId}/commands",
            new { commandType, payloadJson });

    public Task<ApiResult<List<DeviceHealthApi>>> ObterSaudeDevicesAsync() =>
        api.GetAsync<List<DeviceHealthApi>>($"mobile/operation/devices-health?empresaId={GetEmpresaId()}");
}

public class DeviceHealthApi
{
    public string Id { get; set; } = "";
    public string? Label { get; set; }
    public string Status { get; set; } = "";
    public string StatusLabel { get; set; } = "";
    public DateTime? LastSeenAt { get; set; }
    public string? LastSeenIp { get; set; }
    public int PendingCommands { get; set; }
    public int StuckCommands { get; set; }
    public bool Revoked { get; set; }
    public bool PendingPair { get; set; }
}

/// <summary>KPIs do painel /operacao.</summary>
public class OperationDashboardApi
{
    public Guid EmpresaId { get; set; }
    public Guid? LojaId { get; set; }
    public DateTime Generated { get; set; }
    public decimal VendasHojeValor { get; set; }
    public int VendasHojeCount { get; set; }
    public decimal CaixaSaldoHoje { get; set; }
    public decimal CaixaEntradasExtras { get; set; }
    public decimal CaixaSaidas { get; set; }
    public int PedidosAbertos { get; set; }
    public int PedidosPreparando { get; set; }
    public int PedidosProntos { get; set; }
    public int PedidosTravados { get; set; }
    public int ConferenciaPendente { get; set; }
    public int LotesHoje { get; set; }
    public int DivergenciasEstoque { get; set; }
    public int ProdutosPendenteAprovacao { get; set; }
    public int DevicesAtivos { get; set; }
    public int DevicesTotal { get; set; }
}

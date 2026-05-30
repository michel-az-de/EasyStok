using System.Diagnostics;
using System.Reflection;

namespace EasyStock.Api.Controllers;

[ApiController]
[Route("api/admin/status")]
[Authorize(Policy = "SuperAdmin")]
[ResponseCache(Duration = 30)]
public class AdminStatusController(IAdminStatusQueries statusQueries) : EasyStockControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetStatus(CancellationToken ct = default)
    {
        var agora = DateTime.UtcNow;

        var status = await statusQueries.GetStatusAsync(agora, ct);

        // API uptime / version (runtime puro — sem banco)
        var startTime = Process.GetCurrentProcess().StartTime.ToUniversalTime();
        var uptimeSeconds = (long)(agora - startTime).TotalSeconds;
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";

        return DataOk(new
        {
            database = new { status = status.DbStatus, latencyMs = status.DbLatencyMs },
            api = new { status = "ok", uptimeSeconds, version },
            erros24h = new { total = status.Erros24h, ultimaHora = status.Erros1h },
            uso = new { usuariosAtivos24h = status.UsuariosAtivos24h, iaGeracoesMes = status.IaGeracoesMes, ticketsAbertos = status.TicketsAbertos },
            errosRecentes = status.ErrosRecentes,
            ultimaVerificacao = agora
        });
    }
}

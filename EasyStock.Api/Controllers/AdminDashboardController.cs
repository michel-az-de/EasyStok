namespace EasyStock.Api.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Policy = "SuperAdmin")]
public class AdminDashboardController(IAdminDashboardQueries dashboard) : EasyStockControllerBase
{
    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard(CancellationToken ct = default)
        => DataOk(await dashboard.ObterAsync(DateTime.UtcNow, ct));
}

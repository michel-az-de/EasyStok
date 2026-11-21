using EasyStock.Api.Mobile.Services;

namespace EasyStock.Api.Mobile.Controllers;

/// <summary>
/// #2813 (B4): consulta dos fechamentos de caixa do aparelho, SOMENTE LEITURA, para a gestao web (policy Gerente, empresa do token).
/// Controller proprio porque o LegacySnapshotController e [AllowAnonymous] na classe (autentica pela chave do aparelho): um
/// [Authorize] no metodo la nao valeria. O fechamento operacional continua so no Console (#2030); o do aparelho segue ignorado pelo sync.
/// </summary>
[ApiController]
[Authorize(Policy = "Gerente")]
[Route("api/mobile/legacy-import")]
public class LegacyCashClosingsController(LegacyCashClosingsReadService service, ICurrentUserAccessor currentUser) : ControllerBase
{
    [HttpGet("fechamentos")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if (currentUser.EmpresaId == Guid.Empty) return Forbid();
        return Ok(await service.ListAsync(currentUser.EmpresaId, ct));
    }
}

using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.MenuFavoritos;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

/// <summary>
/// Favoritos do menu lateral ("Meu dia") por usuario + loja (ADR-0032, fatia 4).
/// Claims-only: UsuarioId e EmpresaId vem do JWT (CurrentUserAccessor), nunca do cliente
/// — fecha IDOR entre usuarios da mesma empresa. O cliente so informa lojaId + favoritos.
/// </summary>
[SwaggerTag("Menu favorites / Favoritos do menu")]
[ApiController]
[Route("api/preferencias/menu-favoritos")]
[Authorize]
public class PreferenciaMenuController(
    ObterFavoritosMenuUseCase obterUseCase,
    SalvarFavoritosMenuUseCase salvarUseCase,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "Get menu favorites for the current user + store")]
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] Guid lojaId)
    {
        try
        {
            var result = await obterUseCase.ExecuteAsync(
                new ObterFavoritosMenuQuery(currentUser.UsuarioId, currentUser.EmpresaId, lojaId));
            return DataOk(result);
        }
        catch (UseCaseValidationException ex) { return DataBadRequest(ex.Message); }
    }

    [SwaggerOperation(Summary = "Replace menu favorites for the current user + store")]
    [HttpPut]
    public async Task<IActionResult> Put([FromBody] SalvarFavoritosMenuRequest request)
    {
        try
        {
            var favoritos = await salvarUseCase.ExecuteAsync(new SalvarFavoritosMenuCommand(
                currentUser.UsuarioId, currentUser.EmpresaId, request.LojaId,
                request.Favoritos ?? new List<string>()));
            return DataOk(new { favoritos });
        }
        catch (UseCaseValidationException ex) { return DataBadRequest(ex.Message); }
    }

    public sealed record SalvarFavoritosMenuRequest(Guid LojaId, List<string>? Favoritos);
}

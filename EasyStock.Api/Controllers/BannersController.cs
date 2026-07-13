using EasyStock.Application.UseCases.Banners;

namespace EasyStock.Api.Controllers;

/// <summary>
/// Endpoints do app Web para os banners de plataforma (#869). Usuário logado; ids sempre
/// de <c>currentUser</c> (nunca do body → sem IDOR). GET não é cacheável (no-store).
/// </summary>
[ApiController]
[Route("api/banners")]
[Authorize]
public class BannersController(
    ListarBannersAtivosUseCase listarAtivosUseCase,
    RegistrarInteracaoBannerUseCase interagirUseCase,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [HttpGet("ativos")]
    public async Task<IActionResult> Ativos(CancellationToken ct)
    {
        // Correção > flicker: a lista é sempre fresca; o servidor é a fonte da verdade.
        Response.Headers.CacheControl = "no-store";
        var banners = await listarAtivosUseCase.ExecuteAsync(new ListarBannersAtivosQuery(currentUser.UsuarioId), ct);
        return DataOk(banners);
    }

    [HttpPost("{id:guid}/confirmar")]
    public Task<IActionResult> Confirmar(Guid id, CancellationToken ct)
        => RegistrarAsync(id, BannerInteracaoTipo.Confirmado, ct);

    [HttpPost("{id:guid}/visto")]
    public Task<IActionResult> Visto(Guid id, CancellationToken ct)
        => RegistrarAsync(id, BannerInteracaoTipo.Visto, ct);

    /// <summary>Registra a exibição (alcance). Idempotente; não altera quais banners aparecem.</summary>
    [HttpPost("{id:guid}/impressao")]
    public Task<IActionResult> Impressao(Guid id, CancellationToken ct)
        => RegistrarAsync(id, BannerInteracaoTipo.Impressao, ct);

    private async Task<IActionResult> RegistrarAsync(Guid id, BannerInteracaoTipo tipo, CancellationToken ct)
    {
        try
        {
            await interagirUseCase.ExecuteAsync(
                new RegistrarInteracaoBannerCommand(id, currentUser.UsuarioId, tipo), ct);
            return NoContent();
        }
        catch (BannerNaoEncontradoException) { return DataNotFound("Banner não encontrado."); }
    }
}

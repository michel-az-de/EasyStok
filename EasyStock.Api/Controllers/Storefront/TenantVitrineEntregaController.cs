using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.Storefront.Entrega;
using Microsoft.AspNetCore.Mvc.Filters;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers.Storefront;

/// <summary>
/// Cadastro de entrega pela própria loja (S45, ADR-0031): janelas, zonas de frete e bloqueios. Como no
/// <see cref="TenantVitrineCardapioController"/>, a loja é sempre a da empresa do token e todo id é
/// conferido contra ela (sem IDOR por construção).
/// </summary>
[SwaggerTag("Store delivery setup (tenant)")]
[ApiController]
[Route("api/minha-vitrine/entrega")]
[Authorize(Policy = "Admin")]
public class TenantVitrineEntregaController(
    CadastroJanelasEntregaUseCase janelas,
    CadastroZonasFreteUseCase zonas,
    CadastroBloqueiosEntregaUseCase bloqueios,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase, IActionFilter
{
    void IActionFilter.OnActionExecuting(ActionExecutingContext context)
    {
        if (currentUser.EmpresaId == Guid.Empty)
            context.Result = DataBadRequest(
                "Sua sessão não está vinculada a uma empresa. Selecione uma empresa para gerenciar a vitrine.");
    }

    void IActionFilter.OnActionExecuted(ActionExecutedContext context) { }

    private Guid Empresa => currentUser.EmpresaId;

    // ── Janelas ────────────────────────────────────────────────────────

    [SwaggerOperation(Summary = "List delivery windows (including inactive)")]
    [HttpGet("janelas")]
    public Task<IActionResult> ListarJanelas(CancellationToken ct) =>
        Tratar(async () => DataOk(await janelas.ListarAsync(Empresa, ct)));

    [SwaggerOperation(Summary = "Create a delivery window")]
    [HttpPost("janelas")]
    public Task<IActionResult> CriarJanela([FromBody] JanelaEntregaInput body, CancellationToken ct) =>
        Tratar(async () => DataOk(await janelas.CriarAsync(Empresa, body, ct)));

    [SwaggerOperation(Summary = "Update a delivery window")]
    [HttpPut("janelas/{id:guid}")]
    public Task<IActionResult> AtualizarJanela(Guid id, [FromBody] JanelaEntregaInput body, CancellationToken ct) =>
        Tratar(async () => DataOk(await janelas.AtualizarAsync(Empresa, id, body, ct)));

    [SwaggerOperation(Summary = "Activate or deactivate a delivery window")]
    [HttpPost("janelas/{id:guid}/ativa")]
    public Task<IActionResult> DefinirJanelaAtiva(Guid id, [FromBody] AtivaBody body, CancellationToken ct) =>
        Tratar(async () => DataOk(await janelas.DefinirAtivaAsync(Empresa, id, body.Ativa, ct)));

    [SwaggerOperation(Summary = "Delete an unused delivery window", Description = "Com pedido ou bloqueio na janela, recusa: pause em vez de excluir.")]
    [HttpDelete("janelas/{id:guid}")]
    public Task<IActionResult> ExcluirJanela(Guid id, CancellationToken ct) =>
        Tratar(async () =>
        {
            await janelas.ExcluirAsync(Empresa, id, ct);
            return NoContent();
        });

    // ── Zonas ──────────────────────────────────────────────────────────

    [SwaggerOperation(Summary = "List shipping zones (including inactive)")]
    [HttpGet("zonas")]
    public Task<IActionResult> ListarZonas(CancellationToken ct) =>
        Tratar(async () => DataOk(await zonas.ListarAsync(Empresa, ct)));

    [SwaggerOperation(Summary = "Create a shipping zone", Description = "Informe a faixa de CEP ou a lista de bairros, e só um dos dois.")]
    [HttpPost("zonas")]
    public Task<IActionResult> CriarZona([FromBody] FreteZonaInput body, CancellationToken ct) =>
        Tratar(async () => DataOk(await zonas.CriarAsync(Empresa, body, ct)));

    [SwaggerOperation(Summary = "Update a shipping zone (data and coverage)")]
    [HttpPut("zonas/{id:guid}")]
    public Task<IActionResult> AtualizarZona(Guid id, [FromBody] FreteZonaInput body, CancellationToken ct) =>
        Tratar(async () => DataOk(await zonas.AtualizarAsync(Empresa, id, body, ct)));

    [SwaggerOperation(Summary = "Activate or deactivate a shipping zone")]
    [HttpPost("zonas/{id:guid}/ativa")]
    public Task<IActionResult> DefinirZonaAtiva(Guid id, [FromBody] AtivaBody body, CancellationToken ct) =>
        Tratar(async () => DataOk(await zonas.DefinirAtivaAsync(Empresa, id, body.Ativa, ct)));

    // ── Bloqueios ──────────────────────────────────────────────────────

    [SwaggerOperation(Summary = "List delivery blocks in a period (max 366 days)")]
    [HttpGet("bloqueios")]
    public Task<IActionResult> ListarBloqueios([FromQuery] DateOnly de, [FromQuery] DateOnly ate, CancellationToken ct) =>
        Tratar(async () => DataOk(await bloqueios.ListarAsync(Empresa, de, ate, ct)));

    [SwaggerOperation(Summary = "Block a day or a single window on a date")]
    [HttpPost("bloqueios")]
    public Task<IActionResult> CriarBloqueio([FromBody] BloqueioEntregaInput body, CancellationToken ct) =>
        Tratar(async () => DataOk(await bloqueios.CriarAsync(Empresa, body, ct)));

    [SwaggerOperation(Summary = "Remove a delivery block")]
    [HttpDelete("bloqueios/{id:guid}")]
    public Task<IActionResult> RemoverBloqueio(Guid id, CancellationToken ct) =>
        Tratar(async () =>
        {
            await bloqueios.RemoverAsync(Empresa, id, ct);
            return NoContent();
        });

    private async Task<IActionResult> Tratar(Func<Task<IActionResult>> acao)
    {
        try
        {
            return await acao();
        }
        catch (LojaSemVitrineException ex)
        {
            return DataNotFound(ex.Message);
        }
        catch (CadastroEntregaNaoEncontradoException ex)
        {
            return DataNotFound(ex.Message);
        }
        catch (UseCaseValidationException ex)
        {
            return DataBadRequest(ex.Message);
        }
        catch (RegraDeDominioVioladaException ex)
        {
            return DataBadRequest(ex.Message);
        }
    }
}

public sealed record AtivaBody(bool Ativa);

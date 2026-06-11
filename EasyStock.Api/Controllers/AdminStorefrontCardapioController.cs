using EasyStock.Application.UseCases.Admin.Storefront.Cardapio.AdicionarCardapioItemAdmin;
using EasyStock.Application.UseCases.Admin.Storefront.Cardapio.EditarCardapioItemAdmin;
using EasyStock.Application.UseCases.Admin.Storefront.Cardapio.ListarCardapioAdmin;
using EasyStock.Application.UseCases.Admin.Storefront.Cardapio.ReordenarCardapioItemAdmin;
using EasyStock.Application.UseCases.Admin.Storefront.Cardapio.ToggleDisponibilidadeCardapioItemAdmin;
using EasyStock.Application.UseCases.Admin.Storefront.Cardapio.ToggleVisibilidadeCardapioItemAdmin;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Exceptions.Storefront;
using Npgsql;

namespace EasyStock.Api.Controllers;

/// <summary>
/// CRUD admin do Cardápio de um Storefront.
/// Aceita SuperAdmin e Admin (tenant). Aninhado em
/// /api/admin/storefronts/{storefrontId}/cardapio — convenção REST.
/// </summary>
[ApiController]
[Route("api/admin/storefronts/{storefrontId:guid}/cardapio")]
[Authorize(Policy = "Admin")]   // "Admin" policy = SuperAdmin | Admin (ADR-0031)
public class AdminStorefrontCardapioController(
    ListarCardapioAdminUseCase listar,
    AdicionarCardapioItemAdminUseCase adicionar,
    EditarCardapioItemAdminUseCase editar,
    ToggleVisibilidadeCardapioItemAdminUseCase toggleVisivel,
    ToggleDisponibilidadeCardapioItemAdminUseCase toggleDisponivel,
    ReordenarCardapioItemAdminUseCase reordenar,
    AdminAuditService audit,
    ICurrentUserAccessor currentUser,
    ILogger<AdminStorefrontCardapioController> logger) : EasyStockControllerBase
{
    // Escopo de empresa do chamador: SuperAdmin = null (cross-tenant liberado por design);
    // Admin tenant = sua própria empresa (use cases barram storefront alheio → 404, não 403).
    // Fecha IDOR cross-tenant (ADR-0031 §3).
    private Guid? EscopoEmpresa() =>
        currentUser.Nivel == NivelAcesso.SuperAdmin ? null : currentUser.EmpresaId;

    public sealed record AdicionarItemRequest(
        Guid? ProdutoId,        // null = item avulso (ADR-0031)
        string? NomePublico,    // obrigatório para avulso; override opcional para vinculado
        string? CategoriaTexto,
        double OrdemExibicao,
        bool Visivel,
        string? DescricaoPublica,
        string? Ingredientes,
        string? Alergenos,
        string? SugestaoMolho,
        string? TempoPreparo,
        string? FotoUrl,
        decimal? PrecoStorefront,
        string? Tag,
        string? PesoExibicao,
        string? FiltrosJson,
        string? Motivo);

    public sealed record EditarItemRequest(
        string? NomePublico,    // override de nome para avulso ou vinculado
        string? CategoriaTexto,
        string? DescricaoPublica,
        string? Ingredientes,
        string? Alergenos,
        string? SugestaoMolho,
        string? TempoPreparo,
        string? FotoUrl,
        decimal? PrecoStorefront,
        string? Tag,
        string? PesoExibicao,
        string? FiltrosJson,
        string? Motivo);

    public sealed record ReordenarRequest(double NovaOrdem);

    [HttpGet]
    public async Task<IActionResult> Listar(Guid storefrontId)
    {
        try
        {
            var result = await listar.ExecuteAsync(new ListarCardapioAdminCommand(storefrontId, EscopoEmpresa()));
            return DataOk(result);
        }
        catch (StorefrontNaoEncontradoException) { return DataNotFound("Storefront não encontrado."); }
    }

    [HttpPost]
    public async Task<IActionResult> Adicionar(Guid storefrontId, [FromBody] AdicionarItemRequest req)
    {
        try
        {
            var result = await adicionar.ExecuteAsync(new AdicionarCardapioItemAdminCommand(
                storefrontId,
                req.ProdutoId,
                req.NomePublico,
                req.CategoriaTexto,
                req.OrdemExibicao,
                req.Visivel,
                req.DescricaoPublica,
                req.Ingredientes,
                req.Alergenos,
                req.SugestaoMolho,
                req.TempoPreparo,
                req.FotoUrl,
                req.PrecoStorefront,
                req.Tag,
                req.PesoExibicao,
                req.FiltrosJson,
                EscopoEmpresa()));

            await audit.LogAsync(
                "AdminAdicionouCardapioItem",
                $"StorefrontId={storefrontId}, ItemId={result.ItemId}, ProdutoId={req.ProdutoId?.ToString() ?? "avulso"}",
                motivo: req.Motivo,
                entidadeAfetadaId: result.ItemId);

            return DataCreated(
                $"/api/admin/storefronts/{storefrontId}/cardapio/{result.ItemId}",
                result);
        }
        catch (StorefrontNaoEncontradoException) { return DataNotFound("Storefront não encontrado."); }
        catch (UseCaseValidationException ex) { return DataBadRequest(ex.Message); }
        catch (RegraDeDominioVioladaException ex) { return UnprocessableEntity(new { error = new { code = "DOMAIN_RULE", message = ex.Message } }); }
        // Tradução de violações de constraint do Postgres → 400 amigável (ADR-0031)
        catch (PostgresException ex) when (ex.SqlState == "23505")
            { return DataBadRequest("Já existe um item com esse nome no cardápio."); }
        catch (PostgresException ex) when (ex.SqlState == "23514")
            { return DataBadRequest("Nome é obrigatório para itens sem produto vinculado."); }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao adicionar item ao cardápio {StorefrontId}", storefrontId);
            return Problem(detail: ex.Message, statusCode: 500);
        }
    }

    [HttpPut("{itemId:guid}")]
    public async Task<IActionResult> Editar(Guid storefrontId, Guid itemId, [FromBody] EditarItemRequest req)
    {
        try
        {
            var result = await editar.ExecuteAsync(new EditarCardapioItemAdminCommand(
                storefrontId,
                itemId,
                req.NomePublico,
                req.CategoriaTexto,
                req.DescricaoPublica,
                req.Ingredientes,
                req.Alergenos,
                req.SugestaoMolho,
                req.TempoPreparo,
                req.FotoUrl,
                req.PrecoStorefront,
                req.Tag,
                req.PesoExibicao,
                req.FiltrosJson,
                EscopoEmpresa()));

            await audit.LogAsync(
                "AdminEditouCardapioItem",
                $"StorefrontId={storefrontId}, ItemId={itemId}",
                motivo: req.Motivo,
                entidadeAfetadaId: itemId);

            return DataOk(result);
        }
        catch (CardapioItemNaoEncontradoException) { return DataNotFound("Item não encontrado."); }
        catch (RegraDeDominioVioladaException ex) { return UnprocessableEntity(new { error = new { code = "DOMAIN_RULE", message = ex.Message } }); }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao editar item {ItemId}", itemId);
            return Problem(detail: ex.Message, statusCode: 500);
        }
    }

    [HttpPost("{itemId:guid}/toggle-visivel")]
    public async Task<IActionResult> ToggleVisivel(Guid storefrontId, Guid itemId)
    {
        try
        {
            var result = await toggleVisivel.ExecuteAsync(new ToggleVisibilidadeCardapioItemAdminCommand(storefrontId, itemId, EscopoEmpresa()));
            await audit.LogAsync(
                "AdminToggleVisivelCardapioItem",
                $"StorefrontId={storefrontId}, ItemId={itemId}, VisivelAgora={result.VisivelAgora}",
                entidadeAfetadaId: itemId);
            return DataOk(result);
        }
        catch (CardapioItemNaoEncontradoException) { return DataNotFound("Item não encontrado."); }
    }

    [HttpPost("{itemId:guid}/toggle-disponivel")]
    public async Task<IActionResult> ToggleDisponivel(Guid storefrontId, Guid itemId)
    {
        try
        {
            var result = await toggleDisponivel.ExecuteAsync(new ToggleDisponibilidadeCardapioItemAdminCommand(storefrontId, itemId, EscopoEmpresa()));
            await audit.LogAsync(
                "AdminToggleDisponivelCardapioItem",
                $"StorefrontId={storefrontId}, ItemId={itemId}, DisponivelAgora={result.DisponivelAgora}",
                entidadeAfetadaId: itemId);
            return DataOk(result);
        }
        catch (CardapioItemNaoEncontradoException) { return DataNotFound("Item não encontrado."); }
    }

    [HttpPost("{itemId:guid}/reordenar")]
    public async Task<IActionResult> Reordenar(Guid storefrontId, Guid itemId, [FromBody] ReordenarRequest req)
    {
        try
        {
            var result = await reordenar.ExecuteAsync(new ReordenarCardapioItemAdminCommand(storefrontId, itemId, req.NovaOrdem, EscopoEmpresa()));
            return DataOk(result);
        }
        catch (CardapioItemNaoEncontradoException) { return DataNotFound("Item não encontrado."); }
        catch (RegraDeDominioVioladaException ex) { return UnprocessableEntity(new { error = new { code = "DOMAIN_RULE", message = ex.Message } }); }
    }
}

using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Admin.Storefront.Cardapio.AdicionarCardapioItemAdmin;
using EasyStock.Application.UseCases.Admin.Storefront.Cardapio.EditarCardapioItemAdmin;
using EasyStock.Application.UseCases.Admin.Storefront.Cardapio.ListarCardapioAdmin;
using EasyStock.Application.UseCases.Admin.Storefront.Cardapio.ObterCardapioItemAdmin;
using EasyStock.Application.UseCases.Admin.Storefront.Cardapio.RemoverCardapioItemAdmin;
using EasyStock.Application.UseCases.Admin.Storefront.Cardapio.ReordenarCardapioItemAdmin;
using EasyStock.Application.UseCases.Admin.Storefront.Cardapio.ToggleDisponibilidadeCardapioItemAdmin;
using EasyStock.Application.UseCases.Admin.Storefront.Cardapio.ToggleVisibilidadeCardapioItemAdmin;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Exceptions.Storefront;
using Microsoft.AspNetCore.Mvc.Filters;
using Npgsql;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Api.Controllers.Storefront;

/// <summary>
/// Gestão self-service do cardápio pelo próprio tenant (ADR-0031). Diferente do
/// <see cref="AdminStorefrontCardapioController"/> (recebe storefrontId na rota e atende
/// SuperAdmin cross-tenant), aqui o storefront é SEMPRE resolvido pela empresa do token —
/// o tenant nunca opera storefront alheio (sem IDOR por construção). É o endpoint consumido
/// pelo BFF EasyStock.Web. Delega aos mesmos use cases admin, passando o EmpresaId do claim
/// como escopo (defesa em profundidade).
/// </summary>
[ApiController]
[Route("api/minha-vitrine")]
[Authorize(Policy = "Admin")]   // SuperAdmin | Admin; SuperAdmin sem empresa → 404 (use o painel admin)
public class TenantVitrineCardapioController(
    IStorefrontRepository storefrontRepository,
    ListarCardapioAdminUseCase listar,
    ObterCardapioItemAdminUseCase obter,
    AdicionarCardapioItemAdminUseCase adicionar,
    EditarCardapioItemAdminUseCase editar,
    ToggleVisibilidadeCardapioItemAdminUseCase toggleVisivel,
    ToggleDisponibilidadeCardapioItemAdminUseCase toggleDisponivel,
    ReordenarCardapioItemAdminUseCase reordenar,
    RemoverCardapioItemAdminUseCase remover,
    ICurrentUserAccessor currentUser,
    ILogger<TenantVitrineCardapioController> logger) : EasyStockControllerBase, IActionFilter
{
    private const string SemVitrine = "Sua vitrine ainda não foi criada. Fale com o suporte.";

    // p2 (auditoria 2026-06-11): sem empresa no token (SuperAdmin, ou Admin com 2+ empresas sem
    // seleção) → EmpresaId=Guid.Empty. Em vez de 404 confuso ("sem vitrine"), 400 com orientação.
    void IActionFilter.OnActionExecuting(ActionExecutingContext context)
    {
        if (currentUser.EmpresaId == Guid.Empty)
            context.Result = DataBadRequest(
                "Sua sessão não está vinculada a uma empresa. Selecione uma empresa para gerenciar a vitrine " +
                "(SuperAdmin: use o painel admin em /api/admin/storefronts).");
    }

    void IActionFilter.OnActionExecuted(ActionExecutedContext context) { }

    public sealed record VitrineResumoResponse(Guid StorefrontId, string Slug, string TituloPublico, bool Ativo);

    public sealed record AdicionarItemRequest(
        Guid? ProdutoId,
        string? NomePublico,
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
        string? FiltrosJson);

    public sealed record EditarItemRequest(
        string? NomePublico,
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
        string? FiltrosJson);

    public sealed record ReordenarRequest(double NovaOrdem);

    // EmpresaId do tenant; Guid.Empty para SuperAdmin (que não tem vitrine própria).
    private Guid EmpresaId => currentUser.EmpresaId;

    // Resolve o storefront da empresa do token. null => sem vitrine (404, não vaza).
    private Task<StorefrontEntity?> ResolverStorefrontAsync() =>
        EmpresaId == Guid.Empty
            ? Task.FromResult<StorefrontEntity?>(null)
            : storefrontRepository.GetByEmpresaAsync(EmpresaId);

    /// <summary>Resumo da vitrine do tenant — usado pelo Web para descobrir slug + link de impressão.</summary>
    [HttpGet]
    public async Task<IActionResult> ObterVitrine()
    {
        var sf = await ResolverStorefrontAsync();
        if (sf is null) return DataNotFound(SemVitrine);
        return DataOk(new VitrineResumoResponse(sf.Id, sf.Slug, sf.TituloPublico, sf.Ativo));
    }

    [HttpGet("cardapio")]
    public async Task<IActionResult> ListarCardapio()
    {
        var sf = await ResolverStorefrontAsync();
        if (sf is null) return DataNotFound(SemVitrine);

        var result = await listar.ExecuteAsync(new ListarCardapioAdminCommand(sf.Id, EmpresaId));
        return DataOk(result);
    }

    /// <summary>Detalhe completo de um item — alimenta o prefill do formulário de edição no Web.</summary>
    [HttpGet("cardapio/{itemId:guid}")]
    public async Task<IActionResult> ObterItem(Guid itemId)
    {
        var sf = await ResolverStorefrontAsync();
        if (sf is null) return DataNotFound(SemVitrine);

        try
        {
            var result = await obter.ExecuteAsync(new ObterCardapioItemAdminCommand(sf.Id, itemId, EmpresaId));
            return DataOk(result);
        }
        catch (CardapioItemNaoEncontradoException) { return DataNotFound("Item não encontrado."); }
    }

    [HttpPost("cardapio")]
    public async Task<IActionResult> Adicionar([FromBody] AdicionarItemRequest req)
    {
        var sf = await ResolverStorefrontAsync();
        if (sf is null) return DataNotFound(SemVitrine);

        try
        {
            var result = await adicionar.ExecuteAsync(new AdicionarCardapioItemAdminCommand(
                sf.Id,
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
                EmpresaId));

            return DataCreated($"/api/minha-vitrine/cardapio/{result.ItemId}", result);
        }
        catch (StorefrontNaoEncontradoException) { return DataNotFound(SemVitrine); }
        catch (UseCaseValidationException ex) { return DataBadRequest(ex.Message); }
        catch (RegraDeDominioVioladaException ex) { return UnprocessableEntity(new { error = new { code = "DOMAIN_RULE", message = ex.Message } }); }
        catch (PostgresException ex) when (ex.SqlState == "23505") { return DataBadRequest("Já existe um item com esse nome no cardápio."); }
        catch (PostgresException ex) when (ex.SqlState == "23514") { return DataBadRequest("Nome é obrigatório para itens sem produto vinculado."); }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao adicionar item à vitrine {StorefrontId}", sf.Id);
            return Problem(detail: ex.Message, statusCode: 500);
        }
    }

    [HttpPut("cardapio/{itemId:guid}")]
    public async Task<IActionResult> Editar(Guid itemId, [FromBody] EditarItemRequest req)
    {
        var sf = await ResolverStorefrontAsync();
        if (sf is null) return DataNotFound(SemVitrine);

        try
        {
            var result = await editar.ExecuteAsync(new EditarCardapioItemAdminCommand(
                sf.Id,
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
                EmpresaId));

            return DataOk(result);
        }
        catch (CardapioItemNaoEncontradoException) { return DataNotFound("Item não encontrado."); }
        catch (RegraDeDominioVioladaException ex) { return UnprocessableEntity(new { error = new { code = "DOMAIN_RULE", message = ex.Message } }); }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao editar item {ItemId} da vitrine", itemId);
            return Problem(detail: ex.Message, statusCode: 500);
        }
    }

    [HttpPost("cardapio/{itemId:guid}/toggle-visivel")]
    public async Task<IActionResult> ToggleVisivel(Guid itemId)
    {
        var sf = await ResolverStorefrontAsync();
        if (sf is null) return DataNotFound(SemVitrine);

        try
        {
            var result = await toggleVisivel.ExecuteAsync(
                new ToggleVisibilidadeCardapioItemAdminCommand(sf.Id, itemId, EmpresaId));
            return DataOk(result);
        }
        catch (CardapioItemNaoEncontradoException) { return DataNotFound("Item não encontrado."); }
    }

    [HttpPost("cardapio/{itemId:guid}/toggle-disponivel")]
    public async Task<IActionResult> ToggleDisponivel(Guid itemId)
    {
        var sf = await ResolverStorefrontAsync();
        if (sf is null) return DataNotFound(SemVitrine);

        try
        {
            var result = await toggleDisponivel.ExecuteAsync(
                new ToggleDisponibilidadeCardapioItemAdminCommand(sf.Id, itemId, EmpresaId));
            return DataOk(result);
        }
        catch (CardapioItemNaoEncontradoException) { return DataNotFound("Item não encontrado."); }
    }

    [HttpPost("cardapio/{itemId:guid}/reordenar")]
    public async Task<IActionResult> Reordenar(Guid itemId, [FromBody] ReordenarRequest req)
    {
        var sf = await ResolverStorefrontAsync();
        if (sf is null) return DataNotFound(SemVitrine);

        try
        {
            var result = await reordenar.ExecuteAsync(
                new ReordenarCardapioItemAdminCommand(sf.Id, itemId, req.NovaOrdem, EmpresaId));
            return DataOk(result);
        }
        catch (CardapioItemNaoEncontradoException) { return DataNotFound("Item não encontrado."); }
        catch (RegraDeDominioVioladaException ex) { return UnprocessableEntity(new { error = new { code = "DOMAIN_RULE", message = ex.Message } }); }
    }

    [HttpDelete("cardapio/{itemId:guid}")]
    public async Task<IActionResult> Remover(Guid itemId)
    {
        var sf = await ResolverStorefrontAsync();
        if (sf is null) return DataNotFound(SemVitrine);

        try
        {
            var result = await remover.ExecuteAsync(
                new RemoverCardapioItemAdminCommand(sf.Id, itemId, EmpresaId));
            return DataOk(result);
        }
        catch (CardapioItemNaoEncontradoException) { return DataNotFound("Item não encontrado."); }
    }
}

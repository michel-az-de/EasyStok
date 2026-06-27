using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Admin.Storefront.Cardapio;
using EasyStock.Application.UseCases.Admin.Storefront.Cardapio.AdicionarCardapioItemAdmin;
using EasyStock.Application.UseCases.Admin.Storefront.Cardapio.EditarCardapioItemAdmin;
using EasyStock.Application.UseCases.Admin.Storefront.Cardapio.ListarCardapioAdmin;
using EasyStock.Application.UseCases.Admin.Storefront.Cardapio.ObterCardapioItemAdmin;
using EasyStock.Application.UseCases.Admin.Storefront.Cardapio.RemoverCardapioItemAdmin;
using EasyStock.Application.UseCases.Admin.Storefront.Cardapio.ReordenarCardapioItemAdmin;
using EasyStock.Application.UseCases.Admin.Storefront.CriarStorefrontAdmin;
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
    CriarStorefrontAdminUseCase criarStorefront,
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

    public sealed record CriarVitrineRequest(string TituloPublico, string Slug, decimal? PedidoMinimoEntrega);

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
        string? FiltrosJson,
        List<CardapioItemVariacaoInput>? Opcoes,   // ADR-0035 (#652)
        Guid? SecaoId);

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
        string? FiltrosJson,
        List<CardapioItemVariacaoInput>? Opcoes,   // ADR-0035 (#652)
        Guid? SecaoId);

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

    /// <summary>
    /// Cria a vitrine do próprio tenant (self-service). 1 por empresa; o slug é o endereço
    /// público (único global). Defaults seguros da factory: Ativo=false (publica depois).
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CriarVitrine([FromBody] CriarVitrineRequest req)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.TituloPublico) || string.IsNullOrWhiteSpace(req.Slug))
            return DataBadRequest("Informe o nome e o endereço da vitrine.");

        try
        {
            var result = await criarStorefront.ExecuteAsync(new CriarStorefrontAdminCommand(
                EmpresaId, req.Slug, req.TituloPublico, req.PedidoMinimoEntrega ?? 0m));

            return DataCreated("/api/minha-vitrine", new { storefrontId = result.StorefrontId, slug = result.Slug });
        }
        catch (EmpresaJaTemStorefrontException) { return DataBadRequest("Você já tem uma vitrine."); }
        catch (StorefrontSlugDuplicadoException) { return DataBadRequest("Esse endereço já está em uso. Escolha outro."); }
        catch (RegraDeDominioVioladaException ex) { return DataBadRequest(ex.Message); }
        catch (UseCaseValidationException ex) { return DataBadRequest(ex.Message); }
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
                EmpresaId,
                req.Opcoes,
                req.SecaoId));

            return DataCreated($"/api/minha-vitrine/cardapio/{result.ItemId}", result);
        }
        catch (StorefrontNaoEncontradoException) { return DataNotFound(SemVitrine); }
        catch (UseCaseValidationException ex) { return DataBadRequest(ex.Message); }
        catch (RegraDeDominioVioladaException ex) { return UnprocessableEntity(new { error = new { code = "DOMAIN_RULE", message = ex.Message } }); }
        catch (PostgresException ex) when (CardapioPostgresErrors.Traduzir(ex) is { } msg) { return DataBadRequest(msg); }
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
                EmpresaId,
                req.Opcoes,
                req.SecaoId));

            return DataOk(result);
        }
        catch (CardapioItemNaoEncontradoException) { return DataNotFound("Item não encontrado."); }
        catch (UseCaseValidationException ex) { return DataBadRequest(ex.Message); }
        catch (RegraDeDominioVioladaException ex) { return UnprocessableEntity(new { error = new { code = "DOMAIN_RULE", message = ex.Message } }); }
        // Editar preserva o comportamento original: 23514 (nome obrigatório) NÃO é tratado aqui
        // e segue para o handler genérico (500) — mesmo padrão do Admin. As demais violações viram 400.
        catch (PostgresException ex) when (ex.SqlState != "23514" && CardapioPostgresErrors.Traduzir(ex) is { } msg) { return DataBadRequest(msg); }
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

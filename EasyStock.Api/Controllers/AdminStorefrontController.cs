using EasyStock.Application.UseCases.Admin.Storefront.AtivarStorefrontAdmin;
using EasyStock.Application.UseCases.Admin.Storefront.CriarStorefrontAdmin;
using EasyStock.Application.UseCases.Admin.Storefront.DesativarStorefrontAdmin;
using EasyStock.Application.UseCases.Admin.Storefront.EditarStorefrontAdmin;
using EasyStock.Application.UseCases.Admin.Storefront.ListarStorefrontsAdmin;
using EasyStock.Application.UseCases.Admin.Storefront.ObterStorefrontAdmin;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Exceptions.Storefront;

namespace EasyStock.Api.Controllers;

/// <summary>
/// CRUD admin de Storefronts (cross-tenant). Toda rota exige Policy SuperAdmin.
/// Auditoria via <see cref="AdminAuditService"/> em todas mutações (LGPD/compliance).
/// </summary>
[ApiController]
[Route("api/admin/storefronts")]
[Authorize(Policy = "SuperAdmin")]
public class AdminStorefrontController(
    ListarStorefrontsAdminUseCase listar,
    ObterStorefrontAdminUseCase obter,
    CriarStorefrontAdminUseCase criar,
    EditarStorefrontAdminUseCase editar,
    AtivarStorefrontAdminUseCase ativar,
    DesativarStorefrontAdminUseCase desativar,
    AdminAuditService audit,
    ILogger<AdminStorefrontController> logger) : EasyStockControllerBase
{
    public sealed record CriarStorefrontRequest(
        Guid EmpresaId,
        string Slug,
        string TituloPublico,
        decimal PedidoMinimoEntrega,
        string? Motivo);

    public sealed record EditarStorefrontRequest(
        string? SubtituloPublico,
        string? LogoUrl,
        string? CorPrimaria,
        string? WhatsappPedidos,
        string? MensagemForaArea,
        decimal? PedidoMinimoEntrega,
        decimal? FreteGratisAcima,
        string? DominioCustom,
        string? ModeloFiscal,
        bool? HabilitarNfeAutomatica,
        Guid? LojaPadraoId,
        string? Motivo);

    public sealed record AtivarDesativarRequest(string? Motivo);

    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] bool? ativo = null)
    {
        var (p, ps) = NormalisePage(page, pageSize);
        var skip = (p - 1) * ps;

        var result = await listar.ExecuteAsync(new ListarStorefrontsAdminCommand(skip, ps, search, ativo));
        return DataPaged(result.Itens, result.Total, p, ps);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Obter(Guid id)
    {
        try
        {
            var detalhe = await obter.ExecuteAsync(new ObterStorefrontAdminCommand(id));
            return DataOk(detalhe);
        }
        catch (StorefrontNaoEncontradoException) { return DataNotFound("Storefront não encontrado."); }
    }

    [HttpPost]
    public async Task<IActionResult> Criar([FromBody] CriarStorefrontRequest req)
    {
        try
        {
            var result = await criar.ExecuteAsync(new CriarStorefrontAdminCommand(
                req.EmpresaId, req.Slug, req.TituloPublico, req.PedidoMinimoEntrega));

            await audit.LogAsync(
                "AdminCriouStorefront",
                $"StorefrontId={result.StorefrontId}, Slug={result.Slug}, EmpresaId={req.EmpresaId}",
                tenantId: req.EmpresaId,
                motivo: req.Motivo,
                entidadeAfetadaId: result.StorefrontId);

            return DataCreated($"/api/admin/storefronts/{result.StorefrontId}", result);
        }
        catch (UseCaseValidationException ex) { return DataBadRequest(ex.Message); }
        catch (EmpresaJaTemStorefrontException ex) { return Conflict(new { error = new { code = "EMPRESA_JA_TEM_STOREFRONT", message = ex.Message, storefrontExistenteId = ex.StorefrontExistenteId } }); }
        catch (StorefrontSlugDuplicadoException ex) { return UnprocessableEntity(new { error = new { code = "SLUG_DUPLICADO", message = ex.Message, slug = ex.Slug } }); }
        catch (RegraDeDominioVioladaException ex) { return UnprocessableEntity(new { error = new { code = "DOMAIN_RULE", message = ex.Message } }); }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao criar storefront");
            return Problem(detail: ex.Message, statusCode: 500, title: "Erro ao criar storefront.");
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Editar(Guid id, [FromBody] EditarStorefrontRequest req)
    {
        try
        {
            var result = await editar.ExecuteAsync(new EditarStorefrontAdminCommand(
                id,
                TituloPublico: null, // Reservado — entity não expõe setter atualmente
                SubtituloPublico: req.SubtituloPublico,
                LogoUrl: req.LogoUrl,
                CorPrimaria: req.CorPrimaria,
                WhatsappPedidos: req.WhatsappPedidos,
                MensagemForaArea: req.MensagemForaArea,
                PedidoMinimoEntrega: req.PedidoMinimoEntrega,
                FreteGratisAcima: req.FreteGratisAcima,
                DominioCustom: req.DominioCustom,
                ModeloFiscal: req.ModeloFiscal,
                HabilitarNfeAutomatica: req.HabilitarNfeAutomatica,
                LojaPadraoId: req.LojaPadraoId));

            await audit.LogAsync(
                "AdminEditouStorefront",
                $"StorefrontId={id}",
                motivo: req.Motivo,
                entidadeAfetadaId: id);

            return DataOk(result);
        }
        catch (StorefrontNaoEncontradoException) { return DataNotFound("Storefront não encontrado."); }
        catch (RegraDeDominioVioladaException ex) { return UnprocessableEntity(new { error = new { code = "DOMAIN_RULE", message = ex.Message } }); }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao editar storefront {Id}", id);
            return Problem(detail: ex.Message, statusCode: 500, title: "Erro ao editar storefront.");
        }
    }

    [HttpPost("{id:guid}/ativar")]
    public async Task<IActionResult> Ativar(Guid id, [FromBody] AtivarDesativarRequest? req)
    {
        try
        {
            var result = await ativar.ExecuteAsync(new AtivarStorefrontAdminCommand(id));

            await audit.LogAsync("AdminAtivouStorefront", $"StorefrontId={id}",
                motivo: req?.Motivo, entidadeAfetadaId: id);

            return DataOk(result);
        }
        catch (StorefrontNaoEncontradoException) { return DataNotFound("Storefront não encontrado."); }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao ativar storefront {Id}", id);
            return Problem(detail: ex.Message, statusCode: 500);
        }
    }

    [HttpPost("{id:guid}/desativar")]
    public async Task<IActionResult> Desativar(Guid id, [FromBody] AtivarDesativarRequest? req)
    {
        try
        {
            var result = await desativar.ExecuteAsync(new DesativarStorefrontAdminCommand(id));

            await audit.LogAsync("AdminDesativouStorefront", $"StorefrontId={id}",
                motivo: req?.Motivo, entidadeAfetadaId: id);

            return DataOk(result);
        }
        catch (StorefrontNaoEncontradoException) { return DataNotFound("Storefront não encontrado."); }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao desativar storefront {Id}", id);
            return Problem(detail: ex.Message, statusCode: 500);
        }
    }
}

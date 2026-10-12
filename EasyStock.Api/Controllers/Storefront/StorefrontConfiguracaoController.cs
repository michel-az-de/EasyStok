using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Admin.Storefront.AtivarStorefrontAdmin;
using EasyStock.Application.UseCases.Admin.Storefront.DesativarStorefrontAdmin;
using EasyStock.Application.UseCases.Admin.Storefront.EditarStorefrontAdmin;
using EasyStock.Application.UseCases.Admin.Storefront.ObterStorefrontAdmin;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Exceptions.Storefront;
using Microsoft.AspNetCore.Mvc.Filters;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers.Storefront;

/// <summary>
/// Configuração da vitrine pela própria loja (P01-B, #1173/#1176): o que o painel Admin fazia em
/// <c>api/admin/storefronts/{id}</c>, agora no contexto da empresa. A vitrine sai sempre da empresa
/// do token, sem id na rota nem no corpo, então uma empresa não alcança a vitrine de outra por
/// construção. Janelas, zonas e bloqueios ficam em <see cref="TenantVitrineEntregaController"/>;
/// criar a vitrine, em <see cref="TenantVitrineCardapioController"/>.
/// </summary>
[SwaggerTag("Store settings (tenant)")]
[ApiController]
[Route("api/minha-vitrine/configuracao")]
[Authorize(Policy = "Admin")]
public class StorefrontConfiguracaoController(
    IStorefrontRepository storefrontRepository,
    ObterStorefrontAdminUseCase obter,
    EditarStorefrontAdminUseCase editar,
    AtivarStorefrontAdminUseCase ativar,
    DesativarStorefrontAdminUseCase desativar,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase, IActionFilter
{
    private const string SemVitrine = "Sua vitrine ainda não foi criada.";

    /// <summary>Campos nulos não mudam; string vazia limpa o campo de branding.</summary>
    public sealed record EditarConfiguracaoVitrineRequest(
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
        Guid? LojaPadraoId);

    void IActionFilter.OnActionExecuting(ActionExecutingContext context)
    {
        if (currentUser.EmpresaId == Guid.Empty)
            context.Result = DataBadRequest(
                "Sua sessão não está vinculada a uma empresa. Selecione uma empresa para configurar a vitrine.");
    }

    void IActionFilter.OnActionExecuted(ActionExecutedContext context) { }

    private Task<EasyStock.Domain.Entities.Storefront.Storefront?> VitrineDaEmpresaAsync() =>
        storefrontRepository.GetByEmpresaAsync(currentUser.EmpresaId);

    [SwaggerOperation(Summary = "Get the store settings of the current company")]
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Obter()
    {
        var vitrine = await VitrineDaEmpresaAsync();
        if (vitrine is null) return DataNotFound(SemVitrine);
        return DataOk(await obter.ExecuteAsync(new ObterStorefrontAdminCommand(vitrine.Id)));
    }

    [SwaggerOperation(Summary = "Update the store settings of the current company")]
    [HttpPut]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Editar([FromBody] EditarConfiguracaoVitrineRequest req)
    {
        if (req is null) return DataBadRequest("Informe os campos a alterar.");

        var vitrine = await VitrineDaEmpresaAsync();
        if (vitrine is null) return DataNotFound(SemVitrine);

        try
        {
            await editar.ExecuteAsync(new EditarStorefrontAdminCommand(
                vitrine.Id,
                TituloPublico: null, // a entidade não tem setter; o título nasce na criação
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
        }
        catch (StorefrontNaoEncontradoException) { return DataNotFound(SemVitrine); }
        catch (DominioCustomEmUsoException ex) { return DataConflict(ex.Message); }
        catch (RegraDeDominioVioladaException ex) { return DataBadRequest(ex.Message); }
        catch (UseCaseValidationException ex) { return DataBadRequest(ex.Message); }

        return DataOk(await obter.ExecuteAsync(new ObterStorefrontAdminCommand(vitrine.Id)));
    }

    [SwaggerOperation(Summary = "Publish the store of the current company")]
    [HttpPost("ativar")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Ativar()
    {
        var vitrine = await VitrineDaEmpresaAsync();
        if (vitrine is null) return DataNotFound(SemVitrine);
        return DataOk(await ativar.ExecuteAsync(new AtivarStorefrontAdminCommand(vitrine.Id)));
    }

    [SwaggerOperation(Summary = "Unpublish the store of the current company")]
    [HttpPost("desativar")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Desativar()
    {
        var vitrine = await VitrineDaEmpresaAsync();
        if (vitrine is null) return DataNotFound(SemVitrine);
        return DataOk(await desativar.ExecuteAsync(new DesativarStorefrontAdminCommand(vitrine.Id)));
    }
}

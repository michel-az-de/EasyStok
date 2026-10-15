using EasyStock.Application.UseCases.FeatureFlags;

namespace EasyStock.Api.Controllers;

/// <summary>
/// Features ativas do tenant logado (ADR-0048): quais módulos esta empresa enxerga.
///
/// <para>
/// Claims-only — o <c>empresaId</c> sai do JWT, nunca do cliente, então não há como pedir as
/// flags de outra empresa. Endpoint próprio em vez de carona no payload do menu porque flag
/// é por <b>empresa</b>, enquanto o menu é cacheado por usuário e loja; e porque o gate de
/// rota precisa das flags em requisições que não renderizam a barra lateral.
/// </para>
/// </summary>
[ApiController]
[Route("api/feature-flags")]
[Authorize]
public class FeatureFlagsController(
    ObterFeaturesAtivasUseCase obterAtivasUseCase,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    /// <summary>Nomes das features ativas da empresa do usuário logado.</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Ativas(CancellationToken ct)
    {
        // Sem cache no cliente: desligar um módulo tem que valer no próximo request, não
        // quando o navegador resolver revalidar. O BFF é quem cacheia, por 5 minutos.
        Response.Headers.CacheControl = "no-store";

        var features = await obterAtivasUseCase.ExecuteAsync(
            new ObterFeaturesAtivasQuery(currentUser.EmpresaId), ct);

        return DataOk(features);
    }
}

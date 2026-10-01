using EasyStock.Api.Configuration;
using EasyStock.Application.Ports.Output.Integration.Crypto;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.Integracoes;
using Microsoft.AspNetCore.RateLimiting;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

/// <summary>
/// Integrações da loja (F16, #1246): Mercado Pago, WhatsApp, Google Maps e Lalamove. A chave da loja
/// é gravada cifrada (AES-256-GCM) e nunca volta: o GET devolve só se há chave, a máscara dos
/// últimos 4, a origem (loja ou global da FMA) e o último teste. O que é global da app (token da
/// Meta, segredo do webhook do Mercado Pago) aparece só como leitura.
/// </summary>
[SwaggerTag("Store integrations")]
[ApiController]
[Route("api/integracoes")]
[Authorize(Policy = "Admin")]
public class IntegracoesController(
    ListarIntegracoesUseCase listarUseCase,
    SalvarChaveIntegracaoUseCase salvarUseCase,
    TestarIntegracaoUseCase testarUseCase,
    DesativarIntegracaoUseCase desativarUseCase,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "List the store integrations (never returns secrets)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [HttpGet]
    public async Task<IActionResult> Listar(CancellationToken ct)
        => DataOk(await listarUseCase.ExecuteAsync(currentUser.EmpresaId, ct));

    [SwaggerOperation(Summary = "Save the store key for a provider (encrypted at rest)",
        Description = "Campos por provider: mercadopago {accessToken}; googlemaps {apiKey}; lalamove {apiKey, apiSecret} + ambiente sandbox|producao. WhatsApp é gerido pela FMA (400). Sem KEK no ambiente, 503.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    [HttpPut("{provider}")]
    public async Task<IActionResult> Salvar(string provider, [FromBody] SalvarChaveIntegracaoBody body, CancellationToken ct)
    {
        try
        {
            await salvarUseCase.ExecuteAsync(new SalvarChaveIntegracaoCommand(
                currentUser.EmpresaId, currentUser.UsuarioId, provider,
                body.Campos ?? new Dictionary<string, string?>(), body.Ambiente, body.ValidoAte), ct);
            return DataOk(await ItemAsync(provider, ct));
        }
        catch (IntegracaoNaoEncontradaException ex)
        {
            return DataNotFound(ex.Message);
        }
        catch (UseCaseValidationException ex)
        {
            return DataBadRequest(ex.Message);
        }
        catch (ChaveMestraAusenteException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                error = new
                {
                    code = "KEK_AUSENTE",
                    message = "A chave-mestra das integrações não está configurada neste ambiente (EZ_CRYPTO_KEK_ID e EZ_CRYPTO_KEK). Fale com a FMA.",
                },
            });
        }
    }

    [SwaggerOperation(Summary = "Test the connection with the key in use (5 s cap, 6/min per store and provider)",
        Description = "Chamada sem efeito colateral ao provedor; o resultado é gravado e aparece no GET.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [EnableRateLimiting(IntegracaoTesteRateLimit.Politica)]
    [HttpPost("{provider}/testar")]
    public async Task<IActionResult> Testar(string provider, CancellationToken ct)
    {
        try
        {
            return DataOk(await testarUseCase.ExecuteAsync(currentUser.EmpresaId, provider, ct));
        }
        catch (IntegracaoNaoEncontradaException ex)
        {
            return DataNotFound(ex.Message);
        }
        catch (UseCaseValidationException ex)
        {
            return DataBadRequest(ex.Message);
        }
    }

    [SwaggerOperation(Summary = "Turn off the store key for a provider",
        Description = "Com chave global da FMA, a integração volta a usá-la. WhatsApp é gerido pela FMA (400).")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpPost("{provider}/desativar")]
    public async Task<IActionResult> Desativar(string provider, CancellationToken ct)
    {
        try
        {
            await desativarUseCase.ExecuteAsync(currentUser.EmpresaId, provider, ct);
            return DataOk(await ItemAsync(provider, ct));
        }
        catch (IntegracaoNaoEncontradaException ex)
        {
            return DataNotFound(ex.Message);
        }
        catch (UseCaseValidationException ex)
        {
            return DataBadRequest(ex.Message);
        }
    }

    private async Task<IntegracaoResult?> ItemAsync(string provider, CancellationToken ct) =>
        (await listarUseCase.ExecuteAsync(currentUser.EmpresaId, ct))
            .FirstOrDefault(i => string.Equals(i.Provider, provider.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>Corpo do PUT. Os campos são segredo: só entram, nunca voltam.</summary>
public sealed record SalvarChaveIntegracaoBody(Dictionary<string, string?>? Campos, string? Ambiente, DateTime? ValidoAte);

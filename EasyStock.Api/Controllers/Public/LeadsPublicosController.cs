using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.Public;
using Microsoft.AspNetCore.RateLimiting;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers.Public;

public sealed record LeadPublicoRequest(
    [Required, MaxLength(150)] string Nome,
    [Required, EmailAddress, MaxLength(255)] string Email,
    OrigemLead Origem,
    bool ConsentimentoLgpd,
    [property: MaxLength(32)] string? Telefone = null,
    [property: MaxLength(150)] string? Empresa = null,
    [property: MaxLength(5000)] string? Mensagem = null,
    [property: MaxLength(80)] string? TipoNegocio = null,
    bool ReceberNewsletter = false,
    [property: MaxLength(120)] string? UtmSource = null,
    [property: MaxLength(120)] string? UtmMedium = null,
    [property: MaxLength(120)] string? UtmCampaign = null,
    [property: MaxLength(120)] string? Website = null);

public sealed record LeadPublicoResponse(Guid LeadId, string Mensagem);

[SwaggerTag("Public / Captura de leads da landing")]
[ApiController]
[Route("api/public/leads")]
[AllowAnonymous]
public sealed class LeadsPublicosController(
    RegistrarLeadPublicoUseCase useCase,
    ILogger<LeadsPublicosController> logger) : EasyStockControllerBase
{
    [HttpPost]
    [SwaggerOperation(
        Summary = "Registra lead capturado na landing publica",
        Description = "Endpoint anonimo. Anti-spam por honeypot (campo Website) + rate-limit por IP. " +
                     "Retorna 201 mesmo quando descartado por spam pra nao vazar sinal pro bot.")]
    [ProducesResponseType(typeof(LeadPublicoResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Registrar([FromBody] LeadPublicoRequest req)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = HttpContext.Request.Headers.UserAgent.ToString();
        if (userAgent.Length > 512) userAgent = userAgent[..512];

        try
        {
            var result = await useCase.ExecuteAsync(new RegistrarLeadPublicoCommand(
                Nome: req.Nome,
                Email: req.Email,
                Origem: req.Origem,
                ConsentimentoLgpd: req.ConsentimentoLgpd,
                Telefone: req.Telefone,
                Empresa: req.Empresa,
                Mensagem: req.Mensagem,
                TipoNegocio: req.TipoNegocio,
                ReceberNewsletter: req.ReceberNewsletter,
                IpOrigem: ip,
                UserAgent: userAgent,
                UtmSource: req.UtmSource,
                UtmMedium: req.UtmMedium,
                UtmCampaign: req.UtmCampaign,
                Honeypot: req.Website));

            // Resposta neutra mesmo em caso de descarte por spam — bot nao deve
            // distinguir aceito de rejeitado.
            return DataCreated(
                "/api/public/leads",
                new LeadPublicoResponse(
                    result.LeadId,
                    "Recebemos seu contato. Em breve a gente fala com voce."));
        }
        catch (UseCaseValidationException ex)
        {
            logger.LogInformation(ex, "Lead invalido recebido. IP={Ip}", ip);
            return DataBadRequest(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return DataBadRequest(ex.Message);
        }
    }
}

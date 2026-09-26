using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Application.UseCases.Common;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

[SwaggerTag("WhatsApp attendance configuration")]
[ApiController]
[Route("api/atendimento/configuracao")]
[Authorize(Policy = "Admin")]
public class AtendimentoConfiguracaoController(
    ObterConfiguracaoAtendimentoUseCase obterUseCase,
    AtualizarConfiguracaoAtendimentoUseCase atualizarUseCase,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "Get WhatsApp attendance configuration (Admin only)",
        Description = "Devolve o padrão em memória quando a empresa ainda não tem registro — nunca 404.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet]
    public async Task<IActionResult> Get()
        => DataOk(await obterUseCase.ExecuteAsync(new ObterConfiguracaoAtendimentoQuery(currentUser.EmpresaId)));

    [SwaggerOperation(Summary = "Update WhatsApp attendance configuration (Admin only)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpPut]
    public async Task<IActionResult> Put([FromBody] AtualizarConfiguracaoAtendimentoBody body)
    {
        try
        {
            var resultado = await atualizarUseCase.ExecuteAsync(new AtualizarConfiguracaoAtendimentoCommand(
                currentUser.EmpresaId,
                body.Tom,
                body.NivelSugestao,
                body.SaudacaoPrimeiroContato,
                body.SaudacaoRetorno,
                body.FraseEspera,
                body.MensagemForaArea,
                body.RespiroMinutos,
                body.TempoPreparoPadraoMinutos,
                body.Ativo));

            return DataOk(resultado);
        }
        catch (UseCaseValidationException ex)
        {
            return DataBadRequest(ex.Message);
        }
    }
}

public sealed record AtualizarConfiguracaoAtendimentoBody(
    string? Tom,
    EasyStock.Domain.Enums.Atendimento.NivelSugestaoAtendimento? NivelSugestao,
    string? SaudacaoPrimeiroContato,
    string? SaudacaoRetorno,
    string? FraseEspera,
    string? MensagemForaArea,
    int? RespiroMinutos,
    int? TempoPreparoPadraoMinutos,
    bool? Ativo);

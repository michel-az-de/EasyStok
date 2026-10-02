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
    DefinirModeloRetomadaUseCase modeloRetomadaUseCase,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "Set the approved template that reopens a conversation after 24 h (S58, Admin only)",
        Description = "Nome vazio desliga a retomada. O modelo precisa estar aprovado na Meta com uma variável (o primeiro nome).")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [HttpPut("modelo-retomada")]
    public async Task<IActionResult> PutModeloRetomada([FromBody] ModeloRetomadaBody body)
    {
        try
        {
            return DataOk(await modeloRetomadaUseCase.ExecuteAsync(currentUser.EmpresaId, body?.Nome, body?.Idioma));
        }
        catch (UseCaseValidationException ex)
        {
            return DataBadRequest(ex.Message);
        }
    }

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

public sealed record ModeloRetomadaBody(string? Nome, string? Idioma);

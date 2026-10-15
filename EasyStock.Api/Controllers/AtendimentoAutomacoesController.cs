using EasyStock.Application.UseCases.Atendimento.Automacoes;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Enums.Atendimento;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

/// <summary>
/// Mensagens automáticas por gatilho (S42): primeiro contato, fora do horário, loja fechada, pagamento
/// confirmado, pós-entrega e encerramento. Desligar a regra é o rollback. O disparo roda nos handlers
/// do outbox.
/// </summary>
[SwaggerTag("Automatic messages")]
[ApiController]
[Route("api/atendimento/automacoes")]
[Authorize(Policy = "Operador")]
public class AtendimentoAutomacoesController(
    AutomacoesUseCases useCases,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "List automatic messages", Description = "Todos os gatilhos; o que não tem regra vem desligado.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [HttpGet]
    public async Task<IActionResult> Listar(CancellationToken ct)
        => DataOk(await useCases.ListarAsync(currentUser.EmpresaId, ct));

    [SwaggerOperation(Summary = "Create or change the automatic message of a trigger")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [HttpPut("{gatilho}")]
    public async Task<IActionResult> Salvar(GatilhoAutomacao gatilho, [FromBody] AutomacaoBody body, CancellationToken ct)
    {
        try
        {
            return DataOk(await useCases.SalvarAsync(
                new SalvarAutomacaoCommand(currentUser.EmpresaId, gatilho, body.Texto ?? string.Empty, body.Ligada), ct));
        }
        catch (UseCaseValidationException ex)
        {
            return DataBadRequest(ex.Message);
        }
    }
}

public sealed record AutomacaoBody(string? Texto, bool Ligada);

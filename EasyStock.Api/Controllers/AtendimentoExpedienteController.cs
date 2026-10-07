using System.Globalization;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.Storefront.Expediente;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Enums.Storefront;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

/// <summary>
/// Expediente da loja pelo console (S40, ADR-0051): horário por dia, mensagens de "fora do
/// horário" e "loja fechada", e o controle manual de abrir e fechar que vence o relógio.
/// Horário e mensagens são da dona (Admin); abrir e fechar na mão é de gerente para cima, e fechar
/// dentro do horário pede justificativa (#1443, regra no <see cref="DefinirControleExpedienteUseCase"/>).
/// </summary>
[SwaggerTag("Store opening hours and manual open/close")]
[ApiController]
[Route("api/atendimento/expediente")]
[Authorize]
public class AtendimentoExpedienteController(
    ObterExpedienteLojaUseCase obterUseCase,
    AtualizarExpedienteLojaUseCase atualizarUseCase,
    DefinirControleExpedienteUseCase controleUseCase,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "Get store opening hours (Admin only)",
        Description = "Sem registro, devolve o padrão (08–22 h todos os dias, automático) — nunca 404.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [HttpGet]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> Get(CancellationToken ct)
        => DataOk(await obterUseCase.ExecuteAsync(currentUser.EmpresaId, ct));

    [SwaggerOperation(Summary = "Update opening hours and closed messages (Admin only)",
        Description = "Horários omitidos mantêm os atuais; lista vazia = nenhum turno (sempre fora do horário). Horas em HH:mm; fecha antes de abre = vira a meia-noite.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [HttpPut]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> Put([FromBody] AtualizarExpedienteBody body, CancellationToken ct)
    {
        try
        {
            var horarios = body.Horarios?.Select(ParaHorario).ToList();
            var resultado = await atualizarUseCase.ExecuteAsync(new AtualizarExpedienteLojaCommand(
                currentUser.EmpresaId, horarios, body.MensagemForaDoHorario, body.MensagemLojaFechada), ct);
            return DataOk(resultado);
        }
        catch (UseCaseValidationException ex)
        {
            return DataBadRequest(ex.Message);
        }
    }

    [SwaggerOperation(Summary = "Open or close the store manually, or back to automatic (Gerente+)",
        Description = "O manual não volta sozinho: só a dona (Admin) devolve para Automatico. Fechar dentro do horário de funcionamento exige justificativa, que vai para a auditoria e avisa os donos.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [HttpPost("controle")]
    [Authorize(Policy = "Gerente")]
    public async Task<IActionResult> DefinirControle([FromBody] DefinirControleExpedienteBody body, CancellationToken ct)
    {
        try
        {
            var resultado = await controleUseCase.ExecuteAsync(new DefinirControleExpedienteCommand(
                currentUser.EmpresaId, body.Controle, currentUser.UsuarioId, currentUser.Nivel, body.Justificativa), ct);
            return DataOk(resultado);
        }
        catch (UseCaseValidationException ex)
        {
            return DataBadRequest(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new ApiErrorResponse(new ApiError("FORBIDDEN", ex.Message, null, null)));
        }
    }

    private static HorarioFuncionamento ParaHorario(HorarioBody h)
    {
        if (!TimeOnly.TryParseExact(h.Abre, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var abre)
            || !TimeOnly.TryParseExact(h.Fecha, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha))
            throw new UseCaseValidationException($"Horário inválido no dia {h.DiaDaSemana}: use HH:mm.");
        return new HorarioFuncionamento(h.DiaDaSemana, abre, fecha);
    }
}

public sealed record HorarioBody(int DiaDaSemana, string Abre, string Fecha);

public sealed record AtualizarExpedienteBody(
    IReadOnlyList<HorarioBody>? Horarios,
    string? MensagemForaDoHorario,
    string? MensagemLojaFechada);

public sealed record DefinirControleExpedienteBody(ControleManualLoja Controle, string? Justificativa = null);

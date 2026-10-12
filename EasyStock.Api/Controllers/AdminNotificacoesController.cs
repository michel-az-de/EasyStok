using EasyStock.Application.Services.Notifications;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.Notifications;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Api.Controllers;

/// <summary>Corpo do disparo de teste. O destinatário não existe aqui de propósito: é sempre o superadmin que chamou.</summary>
public sealed record DispararTesteRequest(string? Tipo, string? Motivo);

/// <summary>
/// Operação de plataforma das notificações (N13): disparo de teste por tipo do catálogo e consulta do desfecho. Só
/// superadmin (policy <c>SuperAdmin</c> que já existe); a mensagem vai ao e-mail do próprio superadmin.
/// </summary>
[ApiController]
[Route("api/admin/notificacoes")]
[Authorize(Policy = "SuperAdmin")]
public class AdminNotificacoesController(
    DispararTesteNotificacaoUseCase dispararTeste,
    LimitadorDisparoTeste limitador,
    ICurrentUserAccessor currentUser,
    AdminAuditService audit) : EasyStockControllerBase
{
    /// <summary>
    /// Publica o exemplo do tipo com <c>teste: true</c> e responde 202 com o <c>eventoId</c>. Motivo (10 caracteres ou mais)
    /// obrigatório e auditado, tipo fora do catálogo é 400 com a lista dos válidos, e o 11º disparo na hora é 429.
    /// </summary>
    [HttpPost("disparo-teste")]
    public async Task<IActionResult> DispararTeste([FromBody] DispararTesteRequest? req, CancellationToken ct)
    {
        if (!RequestGuards.TryValidarMotivo(req?.Motivo, out var motivo, out var erroMotivo)) return DataBadRequest(erroMotivo!);

        if (!TentarLerTipo(req!.Tipo, out var tipo))
            return TipoForaDoCatalogo(req.Tipo);

        if (!limitador.TentarAdquirir(currentUser.UsuarioId, out var esperar))
        {
            var segundos = (int)Math.Ceiling(esperar.TotalSeconds);
            Response.Headers.Append("Retry-After", segundos.ToString());
            return StatusCode(StatusCodes.Status429TooManyRequests, new ApiErrorResponse(new ApiError(
                "TOO_MANY_REQUESTS",
                $"No máximo {LimitadorDisparoTeste.LimitePorJanela} disparos de teste por hora. Tente de novo em {segundos} s.",
                null, null)));
        }

        Guid eventoId;
        try
        {
            eventoId = await dispararTeste.ExecuteAsync(tipo, ct);
        }
        catch (UseCaseValidationException ex)
        {
            return BadRequest(new ApiErrorResponse(new ApiError(ex.Code ?? "BAD_REQUEST", ex.Message, null, null) { Details = ex.Details }));
        }

        // Sem endereço no registro: o destinatário é o próprio superadmin e a auditoria já guarda quem chamou.
        await audit.LogAsync(
            "AdminDisparouTesteNotificacao",
            $"Tipo={tipo}, EventoId={eventoId}",
            motivo: motivo,
            entidadeAfetadaId: eventoId);

        return Accepted(
            $"/api/admin/notificacoes/disparo-teste/{eventoId}",
            new ApiResponse<object>(new { eventoId, tipo = tipo.ToString() }, new { }));
    }

    /// <summary>Desfecho do disparo: por mensagem do outbox, canal, status, provider, tentativas e erro curto. Sem corpo nem destinatário.</summary>
    [HttpGet("disparo-teste/{eventoId:guid}")]
    public async Task<IActionResult> ConsultarDisparoTeste(Guid eventoId, CancellationToken ct)
    {
        if (!TryEnsureNotEmpty(eventoId, "Evento", out var erro)) return erro!;

        try
        {
            var consulta = await dispararTeste.ConsultarAsync(eventoId, ct);
            return consulta is null ? DataNotFound("Disparo de teste não encontrado.") : DataOk(consulta);
        }
        catch (UseCaseValidationException ex)
        {
            return BadRequest(new ApiErrorResponse(new ApiError(ex.Code ?? "BAD_REQUEST", ex.Message, null, null) { Details = ex.Details }));
        }
    }

    /// <summary>Só o nome do tipo vale (número é recusado) e só se estiver no catálogo, isto é, se tiver exemplo.</summary>
    private static bool TentarLerTipo(string? bruto, out TipoEventoNotificacao tipo)
    {
        tipo = default;
        return !string.IsNullOrWhiteSpace(bruto)
               && !int.TryParse(bruto, out _)
               && Enum.TryParse(bruto.Trim(), ignoreCase: true, out tipo)
               && ExemplosDeEvento.Tipos.Contains(tipo);
    }

    private IActionResult TipoForaDoCatalogo(string? informado) =>
        BadRequest(new ApiErrorResponse(new ApiError(
            "TIPO_FORA_DO_CATALOGO",
            $"Tipo '{informado}' fora do catálogo. Válidos: {string.Join(", ", DispararTesteNotificacaoUseCase.TiposValidos)}.",
            null, null)
        { Details = DispararTesteNotificacaoUseCase.TiposValidos }));
}

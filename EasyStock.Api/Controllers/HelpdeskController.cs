using EasyStock.Api.Http;
using EasyStock.Api.Services.Helpdesk;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.TicketSuporte;
using EasyStock.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

/// <summary>
/// Endpoints transversais de Helpdesk para o cliente da empresa: dashboard
/// agregado, avaliacao CSAT pos-fechamento e relatorio CSV exportavel.
/// Distinto do <c>TicketsController</c> (CRUD de tickets do cliente) e do
/// <c>AdminTicketsController</c> (operacao SuperAdmin).
/// </summary>
[SwaggerTag("Helpdesk")]
[ApiController]
[Route("api/helpdesk")]
[Authorize]
public class HelpdeskController(
    HelpdeskDashboardService dashboardService,
    HelpdeskRelatorioService relatorioService,
    AvaliarTicketClienteUseCase avaliarUseCase,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    /// <summary>
    /// Retorna metricas agregadas de tickets da empresa do usuario autenticado:
    /// contagens por status, SLA vencidos, resolvidos hoje, tempo medio de
    /// resposta/resolucao, satisfacao media (CSAT) e distribuicao por
    /// categoria/prioridade. Filtrado por <c>empresaId</c> do JWT (multi-tenant).
    /// </summary>
    [HttpGet("dashboard")]
    [SwaggerOperation(Summary = "Dashboard agregado de helpdesk da empresa do usuario.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Dashboard(CancellationToken ct)
    {
        if (!currentUser.TemPermissao(Permissao.VisualizarTickets))
            return Forbid();

        var resultado = await dashboardService.ObterAsync(currentUser.EmpresaId, ct);
        return DataOk(resultado);
    }

    /// <summary>
    /// Cliente avalia atendimento (CSAT 1..5 + comentario opcional) de um
    /// ticket Resolvido ou Fechado que ele mesmo abriu. Idempotente: nova
    /// chamada substitui nota anterior.
    /// </summary>
    [HttpPost("tickets/{id:guid}/avaliacao")]
    [SwaggerOperation(Summary = "Avaliacao CSAT pos-atendimento.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Avaliar(Guid id, [FromBody] AvaliarRequest req, CancellationToken ct)
    {
        try
        {
            await avaliarUseCase.ExecuteAsync(
                new AvaliarTicketClienteCommand(id, req.Nota, req.Comentario), ct);
            return NoContent();
        }
        catch (UseCaseValidationException ex) { return DataBadRequest(ex.Message); }
        catch (KeyNotFoundException ex) { return DataNotFound(ex.Message); }
    }

    /// <summary>
    /// Exporta tickets do periodo em CSV (UTF-8 com BOM, separador ;).
    /// Colunas: id, titulo, categoria, prioridade, status, criado_em,
    /// resolvido_em, tempo_resolucao_horas, sla_atendido, nota_csat,
    /// atendente. Multi-tenant via JWT. Formato unico no v1: csv.
    /// </summary>
    [HttpGet("relatorio")]
    [SwaggerOperation(Summary = "Relatorio CSV de tickets no periodo.")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(FileContentResult))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Relatorio(
        [FromQuery] DateTime de,
        [FromQuery] DateTime ate,
        [FromQuery] string formato = "csv",
        CancellationToken ct = default)
    {
        if (!currentUser.TemPermissao(Permissao.VisualizarTickets))
            return Forbid();

        if (!string.Equals(formato, "csv", StringComparison.OrdinalIgnoreCase))
            return DataBadRequest("Formato suportado: csv.");

        if (de == default || ate == default)
            return DataBadRequest("Parametros 'de' e 'ate' sao obrigatorios (yyyy-MM-dd).");

        try
        {
            var bytes = await relatorioService.GerarCsvAsync(currentUser.EmpresaId, de, ate, ct);
            var nome = $"helpdesk-{de:yyyyMMdd}-{ate:yyyyMMdd}.csv";
            return File(bytes, "text/csv; charset=utf-8", nome);
        }
        catch (InvalidOperationException ex) { return DataBadRequest(ex.Message); }
    }

    public sealed record AvaliarRequest(int Nota, string? Comentario);
}

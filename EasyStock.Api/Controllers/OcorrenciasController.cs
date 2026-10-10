using EasyStock.Application.UseCases.Atendimento.Ocorrencias;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Enums.Atendimento;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

/// <summary>Corpo de <c>POST api/ocorrencias</c>: a dona abre na mão.</summary>
public sealed record AbrirOcorrenciaRequest(Guid PedidoId, string? Categoria, string? Relato, Guid? ConversaId);

/// <summary>Corpo de <c>POST api/ocorrencias/{id}/resolver</c>.</summary>
public sealed record ResolverOcorrenciaRequest(string? Resolucao, bool Reembolsar, decimal? Valor, Guid? EmpresaId = null);

/// <summary>
/// Ocorrências de pedido (S27, US-049, US-050). Abertura automática (avaliação negativa, agente) ou pela
/// dona; resolução só aqui, com ou sem reembolso. Estorno recusado pelo gateway devolve 502 e a
/// ocorrência continua aberta. Pedido pago fora do gateway resolve com <c>reembolso_manual_necessario</c>.
/// </summary>
[SwaggerTag("Atendimento: ocorrências")]
[ApiController]
[Route("api/ocorrencias")]
[Authorize(Policy = "Operador")]
[ValidateEmpresaId]
public sealed class OcorrenciasController(
    ConsultarOcorrenciasUseCase consultar,
    AbrirOcorrenciaUseCase abrir,
    ResolverOcorrenciaUseCase resolver,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "Listar ocorrências (status=aberta|resolvida)")]
    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] string? status, CancellationToken ct)
    {
        StatusOcorrencia? filtro = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!OcorrenciaDto.TryParse<StatusOcorrencia>(status, out var s))
                return DataBadRequest("status deve ser aberta ou resolvida.");
            filtro = s;
        }
        return DataOk(await consultar.ListarAsync(currentUser.EmpresaId, filtro, ct));
    }

    [SwaggerOperation(Summary = "Obter ocorrência")]
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Obter(Guid id, CancellationToken ct)
    {
        var o = await consultar.ObterAsync(currentUser.EmpresaId, id, ct);
        return o is null ? DataNotFound("Ocorrência não encontrada.") : DataOk(o);
    }

    [SwaggerOperation(Summary = "Abrir ocorrência na mão (origem dona)")]
    [HttpPost]
    public async Task<IActionResult> Abrir([FromBody] AbrirOcorrenciaRequest? body, CancellationToken ct)
    {
        if (body is null) return DataBadRequest("Corpo obrigatório.");
        var categoria = CategoriaOcorrencia.Outro;
        if (!string.IsNullOrWhiteSpace(body.Categoria) && !OcorrenciaDto.TryParse(body.Categoria, out categoria))
            return DataBadRequest("categoria deve ser produto_improprio, atraso, preferencia ou outro.");

        try
        {
            var o = await abrir.ExecuteAsync(new AbrirOcorrenciaInput(
                currentUser.EmpresaId, body.PedidoId, OrigemOcorrencia.Dona, categoria, body.Relato ?? string.Empty, body.ConversaId), ct);
            return DataCreated($"api/ocorrencias/{o.Id}", o);
        }
        catch (UseCaseValidationException ex) { return DataBadRequest(ex.Message); }
    }

    [SwaggerOperation(Summary = "Resolver ocorrência, com ou sem reembolso")]
    [HttpPost("{id:guid}/resolver")]
    public async Task<IActionResult> Resolver(Guid id, [FromBody] ResolverOcorrenciaRequest? body, CancellationToken ct)
    {
        if (body is null) return DataBadRequest("Corpo obrigatório.");
        try
        {
            var r = await resolver.ExecuteAsync(new ResolverOcorrenciaInput(
                currentUser.EmpresaId, id, currentUser.UsuarioId, body.Resolucao ?? string.Empty, body.Reembolsar, body.Valor, currentUser.Nivel, User.FindFirst("nome")?.Value), ct);
            if (r is null) return DataNotFound("Ocorrência não encontrada.");
            if (r.Reembolso?.Situacao == SituacaoReembolso.Falhou)
                return StatusCode(StatusCodes.Status502BadGateway, new { data = r, error = r.Reembolso.Codigo });
            return DataOk(r);
        }
        catch (EasyStock.Application.UseCases.Pedidos.Cobranca.CobrancaPedidoConflitoException ex)
        { return Conflict(new ApiErrorResponse(new ApiError(ex.Codigo, ex.Message, null, null))); }
        catch (UseCaseValidationException ex) { return DataBadRequest(ex.Message); }
    }
}

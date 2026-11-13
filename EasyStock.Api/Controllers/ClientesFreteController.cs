using EasyStock.Application.UseCases.Atendimento.Frete;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Exceptions.Storefront;

namespace EasyStock.Api.Controllers;

[ApiController]
[Route("api/clientes/{clienteId:guid}/frete")]
[Authorize(Policy = "Operador")]
public sealed class ClientesFreteController(ConsultarFreteClienteUseCase frete, ICurrentUserAccessor usuario) : EasyStockControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Consultar(Guid clienteId, [FromQuery] Guid? empresaId, CancellationToken ct)
    {
        // A empresa vem exclusivamente do JWT, inclusive para SuperAdmin nesta consulta do atendimento.
        if (usuario.EmpresaId == Guid.Empty || (empresaId.HasValue && empresaId != usuario.EmpresaId))
            return DataBadRequest("A empresa informada difere da sessão.");
        Response.Headers.CacheControl = "private, no-store";
        try
        {
            var resultado = await frete.ExecuteAsync(usuario.EmpresaId, clienteId, ct);
            return resultado is null ? DataNotFound("Cliente não encontrado.") : DataOk(resultado);
        }
        catch (CepSemCoberturaException ex)
        {
            return StatusCode(StatusCodes.Status422UnprocessableEntity, new ProblemDetails
            {
                Status = StatusCodes.Status422UnprocessableEntity,
                Title = "Frete a combinar",
                Detail = ex.Message
            });
        }
        catch (StorefrontNaoEncontradoException) { return DataNotFound("A empresa não tem vitrine ativa."); }
        catch (CepInvalidoException ex) { return DataBadRequest(ex.Message); }
        catch (UseCaseValidationException ex) { return DataBadRequest(ex); }
    }
}

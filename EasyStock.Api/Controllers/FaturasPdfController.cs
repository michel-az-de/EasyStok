using EasyStock.Application.UseCases.Faturas.GerarPdfFatura;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

/// <summary>
/// Endpoints de PDF de fatura. Cliente baixa apenas suas faturas (filtro
/// automatico por <see cref="ICurrentUserAccessor.EmpresaId"/>); admin com
/// <see cref="Permissao.GerenciarFaturas"/> baixa qualquer fatura.
///
/// <para>
/// Cache: a primeira chamada gera e armazena via <c>IFileStorage</c>;
/// chamadas subsequentes servem do cache. <c>?forcar=true</c> regenera.
/// </para>
/// </summary>
[SwaggerTag("Faturas — PDF")]
[Authorize]
[ApiController]
public class FaturasPdfController(
    GerarPdfFaturaUseCase gerarPdfUseCase,
    ICurrentUserAccessor currentUser,
    ILogger<FaturasPdfController> logger) : EasyStockControllerBase
{
    /// <summary>Cliente: baixa PDF de uma fatura propria.</summary>
    [SwaggerOperation(Summary = "Baixar PDF da fatura (cliente)")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [HttpGet("api/faturas/{id:guid}/pdf")]
    [ValidateEmpresaId]
    public async Task<IActionResult> ClientePdf(
        Guid id,
        [FromQuery] Guid? empresaId,
        [FromQuery] bool forcar = false,
        CancellationToken ct = default)
    {
        if (!currentUser.TemPermissao(Permissao.VisualizarFaturas))
            return Forbid();
        if (!TryResolveEmpresaId(currentUser, empresaId, out var eid, out var err)) return err!;

        var result = await gerarPdfUseCase.ExecuteAsync(
            new GerarPdfFaturaCommand(eid, id, Admin: false, ForcarRegenerar: forcar),
            ct);

        // Retorna 404 (nao 403) quando fatura e de outra empresa — anti-enumeration.
        if (result is null) return DataNotFound("Fatura nao encontrada.");

        return File(result.Bytes, result.ContentType, result.FileName);
    }

    /// <summary>Admin: baixa PDF de qualquer fatura (sujeito a permissao).</summary>
    [SwaggerOperation(Summary = "Baixar PDF da fatura (admin)")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [HttpGet("api/admin/faturas/{id:guid}/pdf")]
    public async Task<IActionResult> AdminPdf(
        Guid id,
        [FromQuery] bool forcar = false,
        CancellationToken ct = default)
    {
        if (!currentUser.TemPermissao(Permissao.VisualizarFaturas))
            return Forbid();

        var result = await gerarPdfUseCase.ExecuteAsync(
            new GerarPdfFaturaCommand(EmpresaId: null, FaturaId: id, Admin: true, ForcarRegenerar: forcar),
            ct);

        if (result is null) return DataNotFound("Fatura nao encontrada.");

        // Admin operacional: bloqueia acesso a fatura de outra empresa. Antes do fix
        // do GetByIdAdminAsync isso era garantido por acidente pelo GlobalQueryFilter;
        // agora o filter foi removido (interface promete "sem filtro EmpresaId") e
        // a checagem precisa ser explicita aqui. SuperAdmin passa por design.
        if (currentUser.Nivel != NivelAcesso.SuperAdmin
            && result.EmpresaId != currentUser.EmpresaId)
        {
            logger.LogWarning(
                "Tentativa de acesso cross-tenant a PDF de fatura. UserEmpresaId={UserEmpresaId} FaturaEmpresaId={FaturaEmpresaId} FaturaId={FaturaId}",
                currentUser.EmpresaId, result.EmpresaId, id);
            return DataNotFound("Fatura nao encontrada.");
        }

        return File(result.Bytes, result.ContentType, result.FileName);
    }
}

using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.GerenciarUploads;
using EasyStock.Domain.Exceptions.Storefront;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

[SwaggerTag("Uploads")]
[Authorize]
[ValidateEmpresaId]
[ApiController]
[Route("api/uploads")]
public class UploadsController(
    GerenciarUploadsUseCase gerenciarUploadsUseCase,
    ICurrentUserAccessor currentUserAccessor) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "Upload product photo")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpPost("produto/{id}/foto")]
    public async Task<IActionResult> UploadFotoProduto(Guid id, [FromQuery] Guid empresaId, IFormFile file, CancellationToken cancellationToken)
    {
        var result = await UploadProdutoAsync(empresaId, id, file, cancellationToken);
        return DataOk(result);
    }

    [SwaggerOperation(Summary = "Upload user avatar")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpPost("usuario/avatar")]
    public async Task<IActionResult> UploadAvatar(IFormFile file, CancellationToken cancellationToken)
    {
        var payload = await LerArquivoAsync(file, cancellationToken);
        var result = await gerenciarUploadsUseCase.UploadAvatarUsuarioAsync(
            currentUserAccessor.UsuarioId,
            payload.FileName,
            payload.ContentType,
            payload.Content,
            cancellationToken);

        return DataOk(result);
    }

    [SwaggerOperation(Summary = "Upload store logo (Admin only)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [Authorize(Policy = "Admin")]
    [HttpPost("loja/logo")]
    public async Task<IActionResult> UploadLogoLoja([FromQuery] Guid lojaId, IFormFile file, CancellationToken cancellationToken)
    {
        var payload = await LerArquivoAsync(file, cancellationToken);
        var result = await gerenciarUploadsUseCase.UploadLogoLojaAsync(
            currentUserAccessor.EmpresaId,
            lojaId,
            payload.FileName,
            payload.ContentType,
            payload.Content,
            cancellationToken);

        return DataOk(result);
    }

    [SwaggerOperation(Summary = "Upload cardapio item photo (Admin only)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [Authorize(Policy = "Admin")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    [HttpPost("cardapio-item/{itemId:guid}/foto")]
    public async Task<IActionResult> UploadFotoCardapioItem(Guid itemId, IFormFile file, CancellationToken cancellationToken)
    {
        // Teto checado ANTES de bufferizar (o [RequestSizeLimit] barra no Kestrel; aqui é o 2o gate).
        if (file is null || file.Length == 0)
            throw new UseCaseValidationException("Arquivo nao informado ou vazio.");
        if (file.Length > 6 * 1024 * 1024)
            throw new UseCaseValidationException("A imagem nao pode ser maior que 6 MB.");

        var payload = await LerArquivoAsync(file, cancellationToken);
        try
        {
            var result = await gerenciarUploadsUseCase.UploadFotoCardapioItemAsync(
                currentUserAccessor.EmpresaId,
                itemId,
                payload.FileName,
                payload.ContentType,
                payload.Content,
                cancellationToken);

            return DataOk(result);
        }
        // IDOR/escopo: item de outra empresa (ou inexistente) → 404, não vaza existência (ADR-0031 §3).
        catch (CardapioItemNaoEncontradoException)
        {
            return DataNotFound("Item de cardápio não encontrado.");
        }
    }

    internal async Task<UploadedFileResult> UploadProdutoAsync(Guid empresaId, Guid produtoId, IFormFile file, CancellationToken cancellationToken)
    {
        var payload = await LerArquivoAsync(file, cancellationToken);
        return await gerenciarUploadsUseCase.UploadFotoProdutoAsync(
            empresaId,
            produtoId,
            payload.FileName,
            payload.ContentType,
            payload.Content,
            cancellationToken);
    }

    private static async Task<(string FileName, string ContentType, byte[] Content)> LerArquivoAsync(IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            throw new UseCaseValidationException("Arquivo nao informado ou vazio.");

        await using var memoryStream = new MemoryStream();
        await file.CopyToAsync(memoryStream, cancellationToken);
        return (file.FileName, file.ContentType, memoryStream.ToArray());
    }
}

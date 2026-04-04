using EasyStock.Application.Ports.Output;
using EasyStock.Application.UseCases.GerenciarUploads;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/uploads")]
public class UploadsController(
    GerenciarUploadsUseCase gerenciarUploadsUseCase,
    ICurrentUserAccessor currentUserAccessor) : ControllerBase
{
    [HttpPost("produto/{id}/foto")]
    public async Task<IActionResult> UploadFotoProduto(Guid id, [FromQuery] Guid empresaId, IFormFile file, CancellationToken cancellationToken)
    {
        var result = await UploadProdutoAsync(empresaId, id, file, cancellationToken);
        return Ok(result);
    }

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

        return Ok(result);
    }

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

        return Ok(result);
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
            throw new InvalidOperationException("Arquivo nao informado.");

        await using var memoryStream = new MemoryStream();
        await file.CopyToAsync(memoryStream, cancellationToken);
        return (file.FileName, file.ContentType, memoryStream.ToArray());
    }
}

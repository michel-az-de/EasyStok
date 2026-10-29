using EasyStock.Application.UseCases.Cliente.Foto;

namespace EasyStock.Api.Controllers;

[ApiController]
[Route("api/clientes/{clienteId:guid}/foto")]
[Authorize(Policy = "Operador")]
public sealed class ClientesFotoController(ClienteFotoUseCase fotos, ICurrentUserAccessor usuario) : EasyStockControllerBase
{
    [HttpPost]
    [RequestSizeLimit(ClienteFotoUseCase.TamanhoMaximo + 65536)]
    [RequestFormLimits(MultipartBodyLengthLimit = ClienteFotoUseCase.TamanhoMaximo + 65536)]
    public async Task<IActionResult> Enviar(Guid clienteId, IFormFile file, [FromQuery] Guid? empresaId, CancellationToken ct)
    {
        if (usuario.EmpresaId == Guid.Empty || (empresaId.HasValue && empresaId != usuario.EmpresaId))
            return DataBadRequest("A empresa informada difere da sessão.");
        var empresa = usuario.EmpresaId;
        if (file is null || file.Length is 0 or > ClienteFotoUseCase.TamanhoMaximo)
            return DataBadRequest("Escolha uma foto de até 6 MB.");
        using var memoria = new MemoryStream();
        await file.CopyToAsync(memoria, ct);
        var result = await fotos.EnviarAsync(empresa, clienteId, usuario.UsuarioId, file.FileName, file.ContentType, memoria.ToArray(), ct);
        return result is null ? DataNotFound("Cliente não encontrado.") : DataOk(result);
    }

    [HttpGet]
    public async Task<IActionResult> Obter(Guid clienteId, [FromQuery] Guid? empresaId, CancellationToken ct)
    {
        if (usuario.EmpresaId == Guid.Empty || (empresaId.HasValue && empresaId != usuario.EmpresaId))
            return DataBadRequest("A empresa informada difere da sessão.");
        var empresa = usuario.EmpresaId;
        var foto = await fotos.ObterAsync(empresa, clienteId, ct);
        if (foto is null) return DataNotFound("Foto não cadastrada.");
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(foto, "image/jpeg");
    }

    [HttpDelete]
    public async Task<IActionResult> Remover(Guid clienteId, [FromQuery] Guid? empresaId, CancellationToken ct)
    {
        if (usuario.EmpresaId == Guid.Empty || (empresaId.HasValue && empresaId != usuario.EmpresaId))
            return DataBadRequest("A empresa informada difere da sessão.");
        var empresa = usuario.EmpresaId;
        var result = await fotos.RemoverAsync(empresa, clienteId, usuario.UsuarioId, ct);
        return result is null ? DataNotFound("Cliente não encontrado.") : DataOk(result);
    }
}

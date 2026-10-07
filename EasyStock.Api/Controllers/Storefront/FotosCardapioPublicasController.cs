using EasyStock.Application.UseCases.Atendimento.Comanda;
using Microsoft.Net.Http.Headers;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers.Storefront;

/// <summary>
/// Foto do cardápio pela API, independente do host gravado na URL (#1448). O console troca
/// <c>https://qualquer-host/files/cardapios/X</c> por <c>api/public/cardapio/fotos/X</c>, que passa pelo
/// <c>/api</c> da mesma origem. Anônimo como o <c>/files</c> que a vitrine já usa, mas restrito a
/// <c>cardapios/</c>: nada de outra pasta do storage sai por aqui.
/// </summary>
[SwaggerTag("Storefront menu photos (public)")]
[ApiController]
[Route("api/public/cardapio/fotos")]
[AllowAnonymous]
public sealed class FotosCardapioPublicasController(ObterFotoCardapioUseCase obterFoto) : EasyStockControllerBase
{
    public static readonly TimeSpan Cache = TimeSpan.FromDays(7);

    [SwaggerOperation(Summary = "Menu photo by storage path (after cardapios/)",
        Description = "404 para caminho fora de cardapios/, com '..', extensão que não é foto ou arquivo ausente.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpGet("{**caminho}")]
    public async Task<IActionResult> Obter(string? caminho, CancellationToken ct = default)
    {
        var foto = await obterFoto.ExecuteAsync(caminho, ct);
        if (foto is null) return DataNotFound("Foto do cardápio não encontrada.");

        // A chave do upload nasce com Guid: o mesmo caminho nunca muda de conteúdo.
        Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue { Public = true, MaxAge = Cache };
        return File(foto.Conteudo, foto.Mime);
    }
}

using System.Text.Json;
using EasyStock.Application.UseCases.Acervo;

namespace EasyStock.Api.Controllers;

[ApiController]
[Authorize(Policy = "Gerente")]
[Route("api/acervo")]
public sealed class AcervoController(AcervoUseCase acervo, ICurrentUserAccessor usuario, EasyStock.Application.Ports.Output.Storage.IImageProcessor processor) : EasyStockControllerBase
{
    [HttpGet("imagens")]
    public async Task<IActionResult> Listar([FromQuery] int pagina = 0, CancellationToken ct = default) =>
        DataOk(await acervo.ListarAsync(usuario.EmpresaId, pagina, ct));
    [HttpGet("imagens/{id:guid}/arquivo")]
    public async Task<IActionResult> Arquivo(Guid id, CancellationToken ct)
    {
        var (imagem, bytes) = await acervo.ArquivoAsync(usuario.EmpresaId, id, ct);
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(bytes, imagem.ContentType);
    }
    [HttpGet("imagens/{id:guid}/miniatura")]
    public async Task<IActionResult> Miniatura(Guid id, CancellationToken ct)
    {
        var (imagem, bytes) = await acervo.ArquivoAsync(usuario.EmpresaId, id, ct);
        var (conteudo, tipo, _) = processor.Optimize(bytes, imagem.ContentType, maxSide: 600, quality: 75);
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(conteudo, tipo);
    }
    [HttpPost("imagens")]
    [RequestSizeLimit(AcervoUseCase.LimiteArquivo + 65536)]
    public async Task<IActionResult> Adicionar(IFormFile file, [FromForm] string dados, CancellationToken ct)
    {
        if (file is null || file.Length == 0 || file.Length > AcervoUseCase.LimiteArquivo) return BadRequest("Envie uma imagem de até 6 MB.");
        DadosImagem entrada;
        try { entrada = JsonSerializer.Deserialize<DadosImagem>(dados, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new JsonException(); }
        catch (JsonException) { return BadRequest("Dados da imagem inválidos."); }
        await using var stream = new MemoryStream(); await file.CopyToAsync(stream, ct);
        var bytes = stream.ToArray();
        using var data = SkiaSharp.SKData.CreateCopy(bytes);
        using var codec = SkiaSharp.SKCodec.Create(data);
        if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0 ||
            codec.Info.Width > 8192 || codec.Info.Height > 8192 || (long)codec.Info.Width * codec.Info.Height > 32000000)
            return BadRequest("Imagem inválida. Use até 8.192 pixels por lado e 32 megapixels.");
        using var bitmap = new SkiaSharp.SKBitmap(codec.Info);
        if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SkiaSharp.SKCodecResult.Success)
            return BadRequest("Não foi possível decodificar a imagem completa.");
        return DataOk(await acervo.AdicionarAsync(usuario.EmpresaId, usuario.UsuarioId, entrada, file.ContentType, bytes, ct));
    }
    [HttpPost("imagens/{id:guid}/aprovar")]
    public async Task<IActionResult> Aprovar(Guid id, CancellationToken ct) => DataOk(await acervo.AprovarAsync(usuario.EmpresaId, usuario.UsuarioId, id, ct));
    [HttpPut("imagens/{id:guid}/canva")]
    public async Task<IActionResult> Canva(Guid id, [FromBody] VinculoCanva req, CancellationToken ct) => DataOk(await acervo.VincularCanvaAsync(usuario.EmpresaId, id, req.Url, req.AssetId, ct));
    [HttpPost("imagens/{id:guid}/publicar")]
    public async Task<IActionResult> Publicar(Guid id, [FromBody] PublicarImagem req, CancellationToken ct) => DataOk(await acervo.PublicarAsync(usuario.EmpresaId, usuario.UsuarioId, id, req, ct));
    [HttpGet("imagens/{id:guid}/historico")]
    public async Task<IActionResult> Historico(Guid id, CancellationToken ct) => DataOk(await acervo.HistoricoAsync(usuario.EmpresaId, id, ct));
    [HttpGet("solicitacoes")]
    public async Task<IActionResult> Solicitacoes(CancellationToken ct) => DataOk(await acervo.SolicitacoesAsync(usuario.EmpresaId, ct));
    [HttpPost("solicitacoes")]
    public async Task<IActionResult> Solicitar([FromBody] SolicitarImagem req, CancellationToken ct) => DataOk(await acervo.SolicitarAsync(usuario.EmpresaId, usuario.UsuarioId, req, ct));
    public sealed record VinculoCanva(string Url, string? AssetId);
}

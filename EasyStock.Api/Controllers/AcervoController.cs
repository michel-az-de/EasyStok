using System.Text.Json;
using EasyStock.Application.UseCases.Acervo;

namespace EasyStock.Api.Controllers;

[ApiController]
[Authorize(Policy = "Gerente")]
[Route("api/acervo")]
public sealed class AcervoController(AcervoUseCase acervo, ICurrentUserAccessor usuario, EasyStock.Application.Ports.Output.Storage.IImageProcessor processor) : EasyStockControllerBase
{
    [HttpGet("imagens")]
    public async Task<IActionResult> Listar([FromQuery] int pagina = 0, [FromQuery] string busca = "", [FromQuery] string origem = "",
        [FromQuery] string formato = "", [FromQuery] string natureza = "", [FromQuery] Guid? itemId = null, [FromQuery] string canal = "", CancellationToken ct = default) =>
        DataOk(await acervo.BuscarAsync(usuario.EmpresaId, pagina, busca, origem, formato, natureza, itemId, canal, ct));
    [HttpGet("cobertura")]
    public async Task<IActionResult> Cobertura(CancellationToken ct) => DataOk(await acervo.CoberturaAsync(usuario.EmpresaId, ct));
    [HttpGet("briefings")]
    public async Task<IActionResult> Briefings(CancellationToken ct) => DataOk(await acervo.BriefingsAsync(usuario.EmpresaId, ct));
    [HttpPost("briefings")]
    public async Task<IActionResult> Briefing([FromBody] DadosBriefing req, CancellationToken ct) =>
        DataOk(await acervo.SalvarBriefingAsync(usuario.EmpresaId, usuario.UsuarioId, req, ct));
    [HttpPost("briefings/{id:guid}/solicitacoes")]
    public async Task<IActionResult> Enfileirar(Guid id, [FromBody] OperacaoBriefing req, CancellationToken ct) =>
        DataOk(await acervo.EnfileirarBriefingAsync(usuario.EmpresaId, usuario.UsuarioId, id, req.OperacaoId, ct));
    [HttpGet("usos")]
    public async Task<IActionResult> Usos(CancellationToken ct) => DataOk(await acervo.UsosAsync(usuario.EmpresaId, ct));
    [HttpPut("imagens/{id:guid}/natureza")]
    public async Task<IActionResult> Natureza(Guid id, [FromBody] ClassificacaoImagem req, CancellationToken ct) =>
        DataOk(await acervo.ClassificarAsync(usuario.EmpresaId, id, req.Natureza, req.NaturezaEsperada, ct));
    [HttpPut("imagens/{id:guid}/uso")]
    public async Task<IActionResult> Uso(Guid id, [FromBody] DadosUso req, CancellationToken ct)
    {
        var (imagem, bytes) = await acervo.ArquivoAsync(usuario.EmpresaId, id, ct);
        if (Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)) != imagem.Sha256)
            return BadRequest("O original não passou na verificação de integridade.");
        using var data = SkiaSharp.SKData.CreateCopy(bytes);
        using var codec = SkiaSharp.SKCodec.Create(data);
        if (codec is null) return BadRequest("Não foi possível conferir as dimensões do original.");
        using var bitmap = new SkiaSharp.SKBitmap(codec.Info);
        if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SkiaSharp.SKCodecResult.Success)
            return BadRequest("Não foi possível decodificar a imagem completa.");
        return DataOk(await acervo.ConfirmarUsoAsync(usuario.EmpresaId, usuario.UsuarioId, id, req, codec.Info.Width, codec.Info.Height, ct));
    }
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
        // O original é imutável (chave por id, deduplicado por hash), então o hash identifica a miniatura.
        // no-cache + ETag: o navegador revalida a cada exibição (autorização e empresa conferidas aqui) e
        // o 304 evita baixar até 6 MB do storage e redimensionar de novo cada cartão da página.
        var meta = await acervo.ObterAsync(usuario.EmpresaId, id, ct);
        var etag = $"\"m600-{meta.Sha256}\"";
        Response.Headers.CacheControl = "private, no-cache";
        Response.Headers.ETag = etag;
        Response.Headers.XContentTypeOptions = "nosniff";
        if (Request.Headers.IfNoneMatch.ToString().Contains(etag, StringComparison.Ordinal))
            return StatusCode(StatusCodes.Status304NotModified);
        var (imagem, bytes) = await acervo.ArquivoAsync(usuario.EmpresaId, id, ct);
        var (conteudo, tipo, _) = processor.Optimize(bytes, imagem.ContentType, maxSide: 600, quality: 75);
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
        // Tipo e assinatura antes de decodificar: GIF ou BMP eram decodificados inteiros na memória
        // só para depois serem recusados, e a recusa dizia "até 8.192 pixels" em vez do tipo aceito.
        if (file.ContentType is not ("image/png" or "image/jpeg" or "image/webp")) return BadRequest("Use PNG, JPEG ou WebP.");
        try { EasyStock.Application.UseCases.GerenciarUploads.UploadSecurityValidator.EnsureContentMatchesDeclaredType(bytes, file.ContentType); }
        catch (InvalidOperationException e) { return BadRequest(e.Message); }
        using var data = SkiaSharp.SKData.CreateCopy(bytes);
        using var codec = SkiaSharp.SKCodec.Create(data);
        if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0 ||
            codec.Info.Width > 8192 || codec.Info.Height > 8192 || (long)codec.Info.Width * codec.Info.Height > 32000000)
            return BadRequest("Imagem inválida. Use até 8.192 pixels por lado e 32 megapixels.");
        using var bitmap = new SkiaSharp.SKBitmap(codec.Info);
        if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SkiaSharp.SKCodecResult.Success)
            return BadRequest("Não foi possível decodificar a imagem completa.");
        return DataOk(await acervo.AdicionarAsync(usuario.EmpresaId, usuario.UsuarioId,
            entrada with { Largura = codec.Info.Width, Altura = codec.Info.Height }, file.ContentType, bytes, ct));
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
    public async Task<IActionResult> Solicitacoes(CancellationToken ct, [FromQuery] bool incluirConcluidas = false) =>
        DataOk(incluirConcluidas ? await acervo.HistoricoFilaAsync(usuario.EmpresaId, ct) : await acervo.SolicitacoesAsync(usuario.EmpresaId, ct));
    [HttpPost("solicitacoes")]
    public async Task<IActionResult> Solicitar([FromBody] SolicitarImagem req, CancellationToken ct) => DataOk(await acervo.SolicitarAsync(usuario.EmpresaId, usuario.UsuarioId, req, ct));
    public sealed record VinculoCanva(string Url, string? AssetId);
    public sealed record OperacaoBriefing(Guid OperacaoId);
    public sealed record ClassificacaoImagem(string Natureza, string NaturezaEsperada);
}

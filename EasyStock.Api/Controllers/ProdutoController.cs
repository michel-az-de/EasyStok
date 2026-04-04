using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.CadastrarProduto;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.GerenciarProduto;
using EasyStock.Application.UseCases.GerenciarUploads;
using EasyStock.Application.UseCases.GerenciarVariacaoProduto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/produtos")]
public class ProdutoController : ControllerBase
{
    private readonly IProdutoRepository _produtoRepository;
    private readonly CadastrarProdutoUseCase _cadastrarProdutoUseCase;
    private readonly GerenciarProdutoUseCase _gerenciarProdutoUseCase;
    private readonly GerenciarVariacaoProdutoUseCase _gerenciarVariacaoProdutoUseCase;
    private readonly GerenciarUploadsUseCase _gerenciarUploadsUseCase;

    public ProdutoController(
        IProdutoRepository produtoRepository,
        CadastrarProdutoUseCase cadastrarProdutoUseCase,
        GerenciarProdutoUseCase gerenciarProdutoUseCase,
        GerenciarVariacaoProdutoUseCase gerenciarVariacaoProdutoUseCase,
        GerenciarUploadsUseCase gerenciarUploadsUseCase)
    {
        _produtoRepository = produtoRepository;
        _cadastrarProdutoUseCase = cadastrarProdutoUseCase;
        _gerenciarProdutoUseCase = gerenciarProdutoUseCase;
        _gerenciarVariacaoProdutoUseCase = gerenciarVariacaoProdutoUseCase;
        _gerenciarUploadsUseCase = gerenciarUploadsUseCase;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] Guid empresaId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var (produtos, totalCount) = await _produtoRepository.GetProdutosPaginadosAsync(empresaId, page, pageSize);
        return Ok(new { Produtos = produtos, TotalCount = totalCount, Page = page, PageSize = pageSize });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id, [FromQuery] Guid empresaId)
    {
        try
        {
            var produto = await _gerenciarProdutoUseCase.ObterDetalheAsync(empresaId, id);
            return Ok(produto);
        }
        catch (UseCaseValidationException)
        {
            return NotFound();
        }
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] Guid empresaId, [FromQuery] string termo)
    {
        var produtos = await _produtoRepository.SearchAsync(empresaId, termo);
        return Ok(produtos);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CadastrarProdutoCommand command)
    {
        var result = await _cadastrarProdutoUseCase.ExecuteAsync(command);
        return CreatedAtAction(nameof(GetById), new { id = result.ProdutoId, empresaId = command.EmpresaId }, result);
    }

    [HttpPatch("{id}")]
    public async Task<IActionResult> Update(Guid id, AtualizarProdutoCommand command)
    {
        if (id != command.ProdutoId)
            return BadRequest(new { Message = "O id da rota nao corresponde ao ProdutoId informado." });

        await _gerenciarProdutoUseCase.AtualizarAsync(command);
        return NoContent();
    }

    [Authorize(Policy = "Admin")]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] Guid empresaId)
    {
        await _gerenciarProdutoUseCase.RemoverAsync(empresaId, id);
        return NoContent();
    }

    [HttpGet("{id}/historico")]
    public async Task<IActionResult> GetHistorico(Guid id, [FromQuery] Guid empresaId)
    {
        var historico = await _gerenciarProdutoUseCase.ObterHistoricoAsync(empresaId, id);
        return Ok(historico);
    }

    [HttpGet("{id}/estatisticas")]
    public async Task<IActionResult> GetEstatisticas(Guid id, [FromQuery] Guid empresaId)
    {
        var estatisticas = await _gerenciarProdutoUseCase.ObterEstatisticasAsync(empresaId, id);
        return Ok(estatisticas);
    }

    [Authorize(Policy = "Gerente")]
    [HttpPost("{id}/variacoes")]
    public async Task<IActionResult> CreateVariacao(Guid id, CriarVariacaoProdutoCommand command)
    {
        if (id != command.ProdutoId)
            return BadRequest(new { Message = "O id da rota nao corresponde ao ProdutoId informado." });

        var result = await _gerenciarVariacaoProdutoUseCase.CriarAsync(command);
        return CreatedAtAction(nameof(GetById), new { id, empresaId = command.EmpresaId }, result);
    }

    [Authorize(Policy = "Gerente")]
    [HttpPatch("{id}/variacoes/{vid}")]
    public async Task<IActionResult> UpdateVariacao(Guid id, Guid vid, AtualizarVariacaoProdutoCommand command)
    {
        if (id != command.ProdutoId || vid != command.VariacaoId)
            return BadRequest(new { Message = "Os ids da rota nao correspondem aos dados informados." });

        await _gerenciarVariacaoProdutoUseCase.AtualizarAsync(command);
        return NoContent();
    }

    [Authorize(Policy = "Admin")]
    [HttpDelete("{id}/variacoes/{vid}")]
    public async Task<IActionResult> DeleteVariacao(Guid id, Guid vid, [FromQuery] Guid empresaId)
    {
        await _gerenciarVariacaoProdutoUseCase.RemoverAsync(empresaId, id, vid);
        return NoContent();
    }

    [Authorize(Policy = "Gerente")]
    [HttpPost("{id}/fotos")]
    public async Task<IActionResult> UploadFoto(Guid id, [FromQuery] Guid empresaId, IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { Message = "Arquivo nao informado." });

        await using var memoryStream = new MemoryStream();
        await file.CopyToAsync(memoryStream, cancellationToken);

        var result = await _gerenciarUploadsUseCase.UploadFotoProdutoAsync(
            empresaId,
            id,
            file.FileName,
            file.ContentType,
            memoryStream.ToArray(),
            cancellationToken);

        return Ok(result);
    }

    [Authorize(Policy = "Gerente")]
    [HttpDelete("{id}/fotos/{fotoId}")]
    public async Task<IActionResult> DeleteFoto(Guid id, Guid fotoId, [FromQuery] Guid empresaId, CancellationToken cancellationToken)
    {
        await _gerenciarUploadsUseCase.RemoverFotoProdutoAsync(empresaId, id, fotoId, cancellationToken);
        return NoContent();
    }
}

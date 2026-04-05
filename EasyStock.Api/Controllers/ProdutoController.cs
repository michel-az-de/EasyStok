using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.CadastrarProduto;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.GerenciarProduto;
using EasyStock.Application.UseCases.GerenciarUploads;
using EasyStock.Application.UseCases.GerenciarVariacaoProduto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

[SwaggerTag("Products / Produtos")]
[Authorize]
[ApiController]
[Route("api/produtos")]
public class ProdutoController(
    IProdutoRepository produtoRepository,
    CadastrarProdutoUseCase cadastrarProdutoUseCase,
    GerenciarProdutoUseCase gerenciarProdutoUseCase,
    GerenciarVariacaoProdutoUseCase gerenciarVariacaoProdutoUseCase,
    GerenciarUploadsUseCase gerenciarUploadsUseCase) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "List products (paginated)", Description = "Returns a paginated list of products for the given company. Supports sorting by nome, marca, status.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid empresaId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? sort = "nome",
        [FromQuery] string? order = "asc")
    {
        var (produtos, totalCount) = await produtoRepository.GetProdutosPaginadosAsync(
            empresaId, page, pageSize, sort, NormaliseOrder(order));
        return DataPaged(produtos, totalCount, page, pageSize);
    }

    [SwaggerOperation(Summary = "Get product details", Description = "Returns full product details including variants, characteristics and packaging.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id, [FromQuery] Guid empresaId)
    {
        try
        {
            return DataOk(await gerenciarProdutoUseCase.ObterDetalheAsync(empresaId, id));
        }
        catch (UseCaseValidationException)
        {
            return DataNotFound();
        }
    }

    [SwaggerOperation(Summary = "Full-text product search", Description = "Searches product name, barcode, brand and SKU.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] Guid empresaId, [FromQuery] string termo)
        => DataOk(await produtoRepository.SearchAsync(empresaId, termo));

    [SwaggerOperation(Summary = "Create new product")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpPost]
    public async Task<IActionResult> Create(CadastrarProdutoCommand command)
    {
        var result = await cadastrarProdutoUseCase.ExecuteAsync(command);
        return DataCreated($"/api/produtos/{result.ProdutoId}", result);
    }

    [SwaggerOperation(Summary = "Update product")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpPatch("{id}")]
    public async Task<IActionResult> Update(Guid id, AtualizarProdutoCommand command)
    {
        if (id != command.ProdutoId)
            return DataBadRequest("O id da rota nao corresponde ao ProdutoId informado.");

        await gerenciarProdutoUseCase.AtualizarAsync(command);
        return NoContent();
    }

    [SwaggerOperation(Summary = "Delete product (Admin only)")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [Authorize(Policy = "Admin")]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] Guid empresaId)
    {
        await gerenciarProdutoUseCase.RemoverAsync(empresaId, id);
        return NoContent();
    }

    [SwaggerOperation(Summary = "Get product stock movement history")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpGet("{id}/historico")]
    public async Task<IActionResult> GetHistorico(Guid id, [FromQuery] Guid empresaId)
        => DataOk(await gerenciarProdutoUseCase.ObterHistoricoAsync(empresaId, id));

    [SwaggerOperation(Summary = "Get product statistics", Description = "Returns sales velocity, average margin, days without movement.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpGet("{id}/estatisticas")]
    public async Task<IActionResult> GetEstatisticas(Guid id, [FromQuery] Guid empresaId)
        => DataOk(await gerenciarProdutoUseCase.ObterEstatisticasAsync(empresaId, id));

    [SwaggerOperation(Summary = "Create product variant (Gerente only)")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [Authorize(Policy = "Gerente")]
    [HttpPost("{id}/variacoes")]
    public async Task<IActionResult> CreateVariacao(Guid id, CriarVariacaoProdutoCommand command)
    {
        if (id != command.ProdutoId)
            return DataBadRequest("O id da rota nao corresponde ao ProdutoId informado.");

        var result = await gerenciarVariacaoProdutoUseCase.CriarAsync(command);
        return DataCreated($"/api/produtos/{id}", result);
    }

    [SwaggerOperation(Summary = "Update product variant (Gerente only)")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [Authorize(Policy = "Gerente")]
    [HttpPatch("{id}/variacoes/{vid}")]
    public async Task<IActionResult> UpdateVariacao(Guid id, Guid vid, AtualizarVariacaoProdutoCommand command)
    {
        if (id != command.ProdutoId || vid != command.VariacaoId)
            return DataBadRequest("Os ids da rota nao correspondem aos dados informados.");

        await gerenciarVariacaoProdutoUseCase.AtualizarAsync(command);
        return NoContent();
    }

    [SwaggerOperation(Summary = "Delete product variant (Admin only)")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [Authorize(Policy = "Admin")]
    [HttpDelete("{id}/variacoes/{vid}")]
    public async Task<IActionResult> DeleteVariacao(Guid id, Guid vid, [FromQuery] Guid empresaId)
    {
        await gerenciarVariacaoProdutoUseCase.RemoverAsync(empresaId, id, vid);
        return NoContent();
    }

    [SwaggerOperation(Summary = "Upload product photo (Gerente only)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [Authorize(Policy = "Gerente")]
    [HttpPost("{id}/fotos")]
    public async Task<IActionResult> UploadFoto(Guid id, [FromQuery] Guid empresaId, IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return DataBadRequest("Arquivo nao informado.");

        await using var memoryStream = new MemoryStream();
        await file.CopyToAsync(memoryStream, cancellationToken);

        var result = await gerenciarUploadsUseCase.UploadFotoProdutoAsync(
            empresaId, id, file.FileName, file.ContentType, memoryStream.ToArray(), cancellationToken);

        return DataOk(result);
    }

    [SwaggerOperation(Summary = "Delete product photo (Gerente only)")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [Authorize(Policy = "Gerente")]
    [HttpDelete("{id}/fotos/{fotoId}")]
    public async Task<IActionResult> DeleteFoto(Guid id, Guid fotoId, [FromQuery] Guid empresaId, CancellationToken cancellationToken)
    {
        await gerenciarUploadsUseCase.RemoverFotoProdutoAsync(empresaId, id, fotoId, cancellationToken);
        return NoContent();
    }
}

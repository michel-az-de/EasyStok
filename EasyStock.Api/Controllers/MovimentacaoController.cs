using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

[SwaggerTag("Stock Movements / Movimentações")]
[Authorize]
[ValidateEmpresaId]
[ApiController]
[Route("api/movimentacoes")]
public class MovimentacaoController(
    IMovimentacaoEstoqueRepository movimentacaoRepository,
    IItemEstoqueRepository itemEstoqueRepository,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "List stock movements (paginated)", Description = "Filter by date range, movement type (ENTRADA/SAIDA) and nature.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid empresaId,
        [FromQuery] DateTime? de,
        [FromQuery] DateTime? ate,
        [FromQuery] TipoMovimentacaoEstoque? tipo,
        [FromQuery] NaturezaMovimentacaoEstoque? natureza,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var resolvedEmpresaId, out var error))
            return error!;

        var (p, ps) = NormalisePage(page, pageSize);
        var (items, totalCount) = await movimentacaoRepository.GetByEmpresaAsync(
            resolvedEmpresaId, de, ate, tipo, natureza, p, ps);

        var dtos = items.Select(m => new
        {
            id = m.Id.ToString(),
            produtoId = m.ProdutoId.ToString(),
            produtoVariacaoId = m.ProdutoVariacaoId?.ToString(),
            vendaId = m.VendaId?.ToString(),
            tipo = m.Tipo.ToString(),
            natureza = m.Natureza.ToString(),
            quantidade = m.Quantidade.Value,
            valorUnitario = m.ValorUnitario != null ? (decimal?)m.ValorUnitario.Valor : null,
            valorTotal = m.ValorTotal != null ? (decimal?)m.ValorTotal.Valor : null,
            dataMovimentacao = m.DataMovimentacao,
            descricao = m.Descricao,
            documentoReferencia = m.DocumentoReferencia,
            estornadaEm = m.EstornadaEm,
            movimentacaoEstornadaId = m.MovimentacaoEstornadaId?.ToString(),
            usuarioId = m.UsuarioId?.ToString(),
            ip = m.Ip,
            userAgent = m.UserAgent,
            dispositivoId = m.DispositivoId,
            motivoEstorno = m.MotivoEstorno,
            produto = m.Produto != null ? new
            {
                id = m.Produto.Id.ToString(),
                sku = m.Produto.SkuBase?.Value ?? "",
                nome = m.Produto.Nome,
                emoji = (string?)null,
                categoria = m.Produto.CategoriaId.ToString(),
                status = m.Produto.Status.ToString()
            } : null,
            produtoVariacao = m.ProdutoVariacao != null ? new
            {
                id = m.ProdutoVariacao.Id.ToString(),
                produtoId = m.ProdutoVariacao.ProdutoId.ToString(),
                nome = m.ProdutoVariacao.Nome
            } : null
        });

        return DataPaged(dtos, totalCount, p, ps);
    }

    [SwaggerOperation(Summary = "Get KPI aggregates for movements", Description = "Returns server-side computed KPIs (total units, revenue, sales count, loss count).")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("kpis")]
    public async Task<IActionResult> GetKpis(
        [FromQuery] Guid empresaId,
        [FromQuery] DateTime? de,
        [FromQuery] DateTime? ate,
        [FromQuery] TipoMovimentacaoEstoque? tipo,
        [FromQuery] NaturezaMovimentacaoEstoque? natureza)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var resolvedEmpresaId, out var error))
            return error!;

        var kpis = await movimentacaoRepository.GetKpisAsync(resolvedEmpresaId, de, ate, tipo, natureza);
        return DataOk(kpis);
    }

    [SwaggerOperation(Summary = "List movements for a specific stock item")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpGet("item/{itemEstoqueId}")]
    public async Task<IActionResult> GetByItem(Guid itemEstoqueId, [FromQuery] Guid empresaId)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var resolvedEmpresaId, out var error))
            return error!;

        var item = await itemEstoqueRepository.GetByIdAsync(resolvedEmpresaId, itemEstoqueId);
        if (item is null)
            return DataNotFound();

        return DataOk(await movimentacaoRepository.GetByItemEstoqueAsync(resolvedEmpresaId, itemEstoqueId));
    }
}

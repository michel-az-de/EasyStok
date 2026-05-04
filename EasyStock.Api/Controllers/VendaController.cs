using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.RegistrarSaidaEstoque;
using EasyStock.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using System.ComponentModel.DataAnnotations;

namespace EasyStock.Api.Controllers;

[SwaggerTag("Sales / Vendas")]
[Authorize]
[ValidateEmpresaId]
[ApiController]
[Route("api/vendas")]
public class VendaController(
    IVendaRepository vendaRepository,
    RegistrarSaidaEstoqueUseCase registrarSaidaUseCase,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "List sales (paginated)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid empresaId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var resolvedEmpresaId, out var error))
            return error!;

        var (p, ps) = NormalisePage(page, pageSize);
        var (vendas, totalCount) = await vendaRepository.GetVendasPorEmpresaAsync(resolvedEmpresaId, p, ps);
        return DataPaged(vendas, totalCount, p, ps);
    }

    [SwaggerOperation(Summary = "Get sale details")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id, [FromQuery] Guid empresaId)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var resolvedEmpresaId, out var error))
            return error!;

        var venda = await vendaRepository.GetByIdAsync(resolvedEmpresaId, id);
        return venda is null ? DataNotFound() : DataOk(venda);
    }

    public sealed record ItemVendaDto(
        Guid Id,
        Guid ItemEstoqueId,
        Guid ProdutoId,
        Guid? ProdutoVariacaoId,
        string? DescricaoSnapshot,
        string? VariacaoSnapshot,
        int Quantidade,
        decimal PrecoUnitario,
        decimal PrecoTotal,
        DateTime CriadoEm);

    [SwaggerOperation(Summary = "Get sale items", Description = "Returns the list of items that compose a sale.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpGet("{id}/itens")]
    public async Task<IActionResult> GetItens(Guid id, [FromQuery] Guid empresaId)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var resolvedEmpresaId, out var error))
            return error!;

        var venda = await vendaRepository.GetByIdAsync(resolvedEmpresaId, id);
        if (venda is null)
            return DataNotFound();

        var itens = (venda.ItensVenda ?? Enumerable.Empty<Domain.Entities.ItemVenda>())
            .Select(i => new ItemVendaDto(
                i.Id,
                i.ItemEstoqueId,
                i.ProdutoId,
                i.ProdutoVariacaoId,
                i.DescricaoSnapshot,
                i.VariacaoSnapshot,
                i.Quantidade.Value,
                i.PrecoUnitario.Valor,
                i.PrecoTotal.Valor,
                i.CriadoEm))
            .ToList();
        return DataOk(itens);
    }

    public sealed record CriarVendaItemRequest(
        [Required] Guid ItemEstoqueId,
        [Range(1, int.MaxValue)] int Quantidade,
        [Range(0.01, double.MaxValue)] decimal PrecoUnitario,
        string? Descricao = null);

    public sealed record CriarVendaRequest(
        [Required] Guid EmpresaId,
        [Required][MinLength(1)] IReadOnlyList<CriarVendaItemRequest> Itens,
        CanalVenda Canal = CanalVenda.LojaPropria,
        NaturezaMovimentacaoEstoque Natureza = NaturezaMovimentacaoEstoque.Venda,
        Guid? LojaId = null,
        string? NumeroNotaFiscal = null,
        string? Observacoes = null,
        DateTime? DataVenda = null);

    [SwaggerOperation(
        Summary = "Create a sale (canonical)",
        Description = "Registers a direct sale, decrements stock, and creates audit movement. " +
                      "Idempotent via Idempotency-Key header.")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpPost]
    public async Task<IActionResult> CriarVenda([FromBody] CriarVendaRequest request)
    {
        if (!TryResolveEmpresaId(currentUser, request.EmpresaId, out var resolvedEmpresaId, out var error))
            return error!;

        var agora = DateTime.UtcNow;
        var dataVenda = request.DataVenda?.ToUniversalTime() ?? agora;

        var command = new RegistrarSaidaEstoqueCommand(
            EmpresaId: resolvedEmpresaId,
            Itens: request.Itens
                .Select(i => new RegistrarSaidaEstoqueItemCommand(
                    i.ItemEstoqueId,
                    i.Quantidade,
                    i.PrecoUnitario,
                    i.Descricao))
                .ToList(),
            DataVenda: dataVenda,
            DataSaida: agora,
            DataEnvio: null,
            NotaFiscal: request.NumeroNotaFiscal,
            Natureza: request.Natureza,
            Canal: request.Canal,
            Observacoes: request.Observacoes);

        var result = await registrarSaidaUseCase.ExecuteAsync(command);

        return DataCreated($"/api/vendas/{result.VendaId}", result);
    }
}

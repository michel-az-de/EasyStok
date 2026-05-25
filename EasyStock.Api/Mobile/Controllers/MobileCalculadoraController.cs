using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.CalcularProducao;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.CriarSugestaoCompra;
using EasyStock.Application.UseCases.PreviewSugestaoCompra;
using EasyStock.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Api.Mobile.Controllers;

/// <summary>
/// Calculadora de Producao do PWA Casa da Baba:
/// - POST /calcular: simula consumo de insumos read-only (1 produto, batch query 1 round trip)
/// - POST /calcular-cesta: Onda 1 in-context — simula varios produtos do mesmo pedido. Tolerante por item (sem receita / erro).
/// - POST /preview-compra: agrupa insumos faltantes por fornecedor (sem criar PFs)
/// - POST /criar-compra: cria N PFs all-or-nothing + outbox por PF. Idempotency-Key obrigatorio (validado UUID),
///   middleware IdempotencyMiddleware faz cache 24h via /api/mobile/calculadora/criar-compra whitelist
/// - GET /produtos-com-receita: lista produtos-finais com receita cadastrada (busca por nome)
/// Tenant guard via MobileManagementControllerBase.
/// </summary>
[ApiController]
[Route("api/mobile/calculadora")]
[Authorize]
public class MobileCalculadoraController(
    CalcularProducaoUseCase calcularUseCase,
    CalcularCestaProducaoUseCase calcularCestaUseCase,
    PreviewSugestaoCompraUseCase previewUseCase,
    CriarSugestaoCompraUseCase criarUseCase,
    IProdutoComposicaoRepository composicaoRepository,
    ICurrentUserAccessor currentUser) : MobileManagementControllerBase(currentUser)
{
    [HttpPost("calcular")]
    public async Task<IActionResult> Calcular(
        [FromBody] CalcularRequest body,
        CancellationToken ct)
    {
        if (!TryResolveEmpresaId(body.EmpresaId, out var emp, out var err)) return err!;

        if (!Enum.TryParse<UnidadeMedida>(body.UnidadeDesejada, true, out var unidadeDesejada))
            throw new UseCaseValidationException("UNIT_INCOMPATIBLE", $"Unidade desejada invalida: {body.UnidadeDesejada}.");

        var result = await calcularUseCase.ExecuteAsync(
            new CalcularProducaoCommand(emp, body.ProdutoFinalId, body.QuantidadeDesejada, unidadeDesejada, body.LojaId),
            ct);

        return Ok(new { data = result });
    }

    [HttpPost("calcular-cesta")]
    public async Task<IActionResult> CalcularCesta(
        [FromBody] CalcularCestaRequest body,
        CancellationToken ct)
    {
        if (!TryResolveEmpresaId(body.EmpresaId, out var emp, out var err)) return err!;

        if (body.Itens == null || body.Itens.Count == 0)
            throw new UseCaseValidationException("EMPTY_CESTA", "Cesta deve ter pelo menos 1 item.");

        var itens = new List<ItemCestaInput>(body.Itens.Count);
        foreach (var i in body.Itens)
        {
            if (!Enum.TryParse<UnidadeMedida>(i.Unidade, true, out var u))
                throw new UseCaseValidationException("UNIT_INCOMPATIBLE", $"Unidade invalida no item: {i.Unidade}.");
            itens.Add(new ItemCestaInput(i.ProdutoFinalId, i.Quantidade, u));
        }

        var result = await calcularCestaUseCase.ExecuteAsync(
            new CalcularCestaProducaoCommand(emp, body.LojaId, itens), ct);

        return Ok(new { data = result });
    }

    [HttpPost("preview-compra")]
    public async Task<IActionResult> PreviewCompra(
        [FromBody] PreviewCompraRequest body,
        CancellationToken ct)
    {
        if (!TryResolveEmpresaId(body.EmpresaId, out var emp, out var err)) return err!;

        var insumos = body.Insumos.Select(i =>
        {
            if (!Enum.TryParse<UnidadeMedida>(i.Unidade, true, out var u))
                throw new UseCaseValidationException("UNIT_INCOMPATIBLE", $"Unidade invalida: {i.Unidade}.");
            return new InsumoFaltanteInput(i.InsumoId, i.QuantidadeFaltante, u);
        }).ToList();

        var result = await previewUseCase.ExecuteAsync(
            new PreviewSugestaoCompraCommand(emp, body.LojaId, insumos), ct);

        return Ok(new { data = result });
    }

    [HttpPost("criar-compra")]
    public async Task<IActionResult> CriarCompra(
        [FromBody] CriarCompraRequest body,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        // Idempotency: middleware ja faz cache 24h via whitelist em Program.cs.
        // Aqui valido o formato UUID v4 pra retornar erro estruturado pro PWA quando mal formado.
        if (string.IsNullOrWhiteSpace(idempotencyKey) || !Guid.TryParse(idempotencyKey, out _))
            throw new UseCaseValidationException("INVALID_IDEMPOTENCY_KEY", "Idempotency-Key deve ser UUID valido.");

        if (!TryResolveEmpresaId(body.EmpresaId, out var emp, out var err)) return err!;

        var fornecedores = body.Fornecedores.Select(g => new FornecedorGrupoInput(
            g.FornecedorId,
            g.Itens.Select(i =>
            {
                if (!Enum.TryParse<UnidadeMedida>(i.Unidade, true, out var u))
                    throw new UseCaseValidationException("UNIT_INCOMPATIBLE", $"Unidade invalida: {i.Unidade}.");
                return new ItemFaltanteInput(i.InsumoId, i.Nome, i.Quantidade, u, i.CustoUnitario, i.Observacao);
            }).ToList()
        )).ToList();

        var result = await criarUseCase.ExecuteAsync(
            new CriarSugestaoCompraCommand(emp, body.LojaId, fornecedores, body.Canal, body.Observacoes, idempotencyKey),
            ct);

        return Ok(new { data = result });
    }

    [HttpGet("produtos-com-receita")]
    public async Task<IActionResult> ProdutosComReceita(
        [FromQuery] Guid? empresaId,
        [FromQuery] string? q,
        [FromQuery] int limit = 20,
        [FromQuery] Guid? lojaId = null,
        CancellationToken ct = default)
    {
        if (!TryResolveEmpresaId(empresaId, out var emp, out var err)) return err!;

        var produtos = await composicaoRepository.BuscarProdutosFinaisAsync(emp, q, limit, lojaId, ct);

        var resultado = produtos.Select(p => new
        {
            id = p.Id,
            nome = p.Nome,
            unidadeMedidaBase = p.UnidadeMedidaBase.ToString(),
            rendimentoBase = p.RendimentoBase,
            rendimentoUnidade = p.RendimentoUnidade.ToString()
        });

        return Ok(new { data = resultado });
    }
}

// === Requests ===

public sealed record CalcularRequest(
    Guid EmpresaId,
    Guid ProdutoFinalId,
    decimal QuantidadeDesejada,
    string UnidadeDesejada,
    Guid? LojaId);

public sealed record CalcularCestaRequest(
    Guid EmpresaId,
    Guid? LojaId,
    List<CalcularCestaItemRequest> Itens);

public sealed record CalcularCestaItemRequest(
    Guid ProdutoFinalId,
    decimal Quantidade,
    string Unidade);

public sealed record PreviewCompraRequest(
    Guid EmpresaId,
    Guid? LojaId,
    List<PreviewItemRequest> Insumos);

public sealed record PreviewItemRequest(
    Guid InsumoId,
    decimal QuantidadeFaltante,
    string Unidade);

public sealed record CriarCompraRequest(
    Guid EmpresaId,
    Guid? LojaId,
    string? Canal,
    string? Observacoes,
    List<FornecedorGrupoRequest> Fornecedores);

public sealed record FornecedorGrupoRequest(
    Guid FornecedorId,
    List<ItemCompraRequest> Itens);

public sealed record ItemCompraRequest(
    Guid InsumoId,
    string Nome,
    decimal Quantidade,
    string Unidade,
    decimal CustoUnitario,
    string? Observacao);

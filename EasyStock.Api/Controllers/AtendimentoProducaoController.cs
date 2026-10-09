using EasyStock.Application.UseCases.Atendimento.Producao;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Exceptions.Storefront;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

public sealed record BaixaAutomaticaRequest(bool Ligada);

public sealed record PlanejamentoRequest(IReadOnlyList<PratoPlanejado>? Pratos);

/// <param name="ItemEstoqueId">Lote escolhido; null = FEFO.</param>
public sealed record PerdaRequest(Guid ProdutoId, Guid? ItemEstoqueId, decimal Quantidade, MotivoPerda Motivo, string? Texto);

public sealed record ProduzirPratosRequest(IReadOnlyList<PratoProduzidoInput>? Pratos, string? Observacao = null);

/// <param name="Unidade">G, Kg, Ml, L, Un...; null = não mexe (no cadastro, Un).</param>
public sealed record InsumoRequest(string? Nome, UnidadeMedida? Unidade, int? Minimo, decimal? Custo, bool? Embalagem = null);

/// <summary>
/// Produção pelo console (M2, plano docs/plan/erp-casa-da-baba/03-m2-producao.md). D-M2-06 (Felipe,
/// 09/10/2026): quem cozinha lança a produção e vê o estoque do dia; ver e mexer no estoque ainda
/// exige a permissão de estoque, como os desacertos (S22).
/// </summary>
[SwaggerTag("Production (console)")]
[ApiController]
[Route("api/atendimento/producao")]
[Authorize(Policy = "Operador")]
public class AtendimentoProducaoController(
    EstoqueDoDiaUseCase estoqueDoDia,
    ProduzirPratosUseCase produzirPratos,
    InsumosDaProducaoUseCase insumos,
    ReceitasDaProducaoUseCase receitas,
    PlanejamentoDaProducaoUseCase planejamento,
    PerdasDaProducaoUseCase perdas,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "Losses of the period by reason and product, in quantity and R$, with each entry (M2.6)",
        Description = "de/ate em dia operacional (padrão: os últimos 7 dias). Não conta ajuste de contagem nem a baixa de insumo da produção.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [HttpGet("perdas")]
    public async Task<IActionResult> Perdas([FromQuery] DateOnly? de, [FromQuery] DateOnly? ate, CancellationToken ct)
    {
        if (!currentUser.TemPermissao(Permissao.GerenciarEstoque)) return Forbid();
        try
        {
            return DataOk(await perdas.ResumoAsync(currentUser.EmpresaId, de, ate, ct));
        }
        catch (UseCaseValidationException ex)
        {
            return DataBadRequest(ex.Message);
        }
    }

    [SwaggerOperation(Summary = "Expired lots with stock, suggested as losses (never posted automatically, M2.6)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [HttpGet("perdas/vencidos")]
    public async Task<IActionResult> Vencidos(CancellationToken ct)
    {
        if (!currentUser.TemPermissao(Permissao.GerenciarEstoque)) return Forbid();
        return DataOk(await perdas.VencidosAsync(currentUser.EmpresaId, ct));
    }

    [SwaggerOperation(Summary = "Post a loss with a reason (D-M2-02) from the chosen lot or FEFO (M2.6)",
        Description = "Acima de R$ 50 pelo custo dos lotes, só Gerente (D-M2-06). Desfazer: POST api/estoque/estorno/{movimentacaoId}.")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [HttpPost("perdas")]
    public async Task<IActionResult> LancarPerda([FromBody] PerdaRequest req, CancellationToken ct)
    {
        if (!currentUser.TemPermissao(Permissao.GerenciarEstoque)) return Forbid();
        var gerente = currentUser.Nivel is NivelAcesso.SuperAdmin or NivelAcesso.Admin or NivelAcesso.Gerente;
        try
        {
            var r = await perdas.LancarAsync(currentUser.EmpresaId, gerente,
                new LancarPerdaInput(req.ProdutoId, req.ItemEstoqueId, req.Quantidade, req.Motivo, req.Texto), ct);
            return DataCreated("/api/atendimento/producao/perdas", r);
        }
        catch (UseCaseValidationException ex) when (ex.Code == PerdasDaProducaoUseCase.CodigoExigeGerente)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ApiErrorResponse(new ApiError(ex.Code, ex.Message, null, null)));
        }
        catch (Exception ex) when (ex is UseCaseValidationException or RegraDeDominioVioladaException)
        {
            return DataBadRequest(ex.Message);
        }
    }

    [SwaggerOperation(Summary = "Production suggestion per menu dish: minimum + scheduled orders + uncovered - stock (M2.5)",
        Description = "ate = último dia dos pedidos agendados (padrão: amanhã, no dia operacional do Brasil).")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [HttpGet("sugestao")]
    public async Task<IActionResult> Sugestao([FromQuery] DateOnly? ate, CancellationToken ct)
    {
        if (!currentUser.TemPermissao(Permissao.GerenciarEstoque)) return Forbid();
        try
        {
            return DataOk(await planejamento.SugestaoAsync(currentUser.EmpresaId, ate, ct));
        }
        catch (StorefrontNaoEncontradoException)
        {
            return DataNotFound("A empresa não tem vitrine ativa.");
        }
        catch (UseCaseValidationException ex)
        {
            return DataBadRequest(ex.Message);
        }
    }

    [SwaggerOperation(Summary = "Supplies needed and shortages for the planned portions (M2.5)",
        Description = "Mesma cesta da calculadora mobile (POST api/mobile/calculadora/calcular-cesta). Não mexe no estoque.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [HttpPost("planejamento")]
    public async Task<IActionResult> Planejar([FromBody] PlanejamentoRequest req, CancellationToken ct)
    {
        if (!currentUser.TemPermissao(Permissao.GerenciarEstoque)) return Forbid();
        try
        {
            return DataOk(await planejamento.PlanejarAsync(currentUser.EmpresaId, req.Pratos, ct));
        }
        catch (UseCaseValidationException ex)
        {
            return DataBadRequest(ex.Message);
        }
    }

    [SwaggerOperation(Summary = "Recipes of the menu dishes with cost per yield unit (M2.4a)",
        Description = "Gravar a receita é o PUT api/produtos/{id}/composicao (Gerente).")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [HttpGet("receitas")]
    public async Task<IActionResult> Receitas(CancellationToken ct)
    {
        if (!currentUser.TemPermissao(Permissao.GerenciarEstoque)) return Forbid();
        try
        {
            return DataOk(await receitas.ListarAsync(currentUser.EmpresaId, ct));
        }
        catch (StorefrontNaoEncontradoException)
        {
            return DataNotFound("A empresa não tem vitrine ativa.");
        }
    }

    [SwaggerOperation(Summary = "One recipe with line costs (unit converted) and cost per yield unit")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [HttpGet("receitas/{produtoId:guid}")]
    public async Task<IActionResult> Receita(Guid produtoId, CancellationToken ct)
    {
        if (!currentUser.TemPermissao(Permissao.GerenciarEstoque)) return Forbid();
        try
        {
            return DataOk(await receitas.ObterAsync(currentUser.EmpresaId, produtoId, ct));
        }
        catch (UseCaseValidationException ex)
        {
            return DataBadRequest(ex.Message);
        }
    }

    [SwaggerOperation(Summary = "Turn the automatic supply deduction on producing this dish on or off (D-M2-01; Gerente)")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [Authorize(Policy = "Gerente")]
    [HttpPut("receitas/{produtoId:guid}/baixa-automatica")]
    public async Task<IActionResult> MarcarBaixaAutomatica(Guid produtoId, [FromBody] BaixaAutomaticaRequest req, CancellationToken ct)
    {
        try
        {
            await receitas.MarcarBaixaAutomaticaAsync(currentUser.EmpresaId, produtoId, req.Ligada, ct);
            return NoContent();
        }
        catch (UseCaseValidationException ex)
        {
            return DataBadRequest(ex.Message);
        }
    }

    [SwaggerOperation(Summary = "Production supplies (EhInsumo) with stock, minimum, cost and recipes using them (M2.3)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [HttpGet("insumos")]
    public async Task<IActionResult> Insumos(CancellationToken ct)
    {
        if (!currentUser.TemPermissao(Permissao.GerenciarEstoque)) return Forbid();
        return DataOk(await insumos.ListarAsync(currentUser.EmpresaId, ct));
    }

    [SwaggerOperation(Summary = "Quick supply registration: name, unit, minimum and cost (Gerente)")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [Authorize(Policy = "Gerente")]
    [HttpPost("insumos")]
    public async Task<IActionResult> CriarInsumo([FromBody] InsumoRequest req, CancellationToken ct)
    {
        try
        {
            var id = await insumos.CriarAsync(currentUser.EmpresaId, currentUser.UsuarioId,
                new InsumoInput(req.Nome, req.Unidade, req.Minimo, req.Custo, req.Embalagem), ct);
            return DataCreated($"/api/produtos/{id}", new { produtoId = id });
        }
        catch (Exception ex) when (ex is UseCaseValidationException or RegraDeDominioVioladaException)
        {
            return DataBadRequest(ex.Message);
        }
    }

    [SwaggerOperation(Summary = "Adjust a supply's minimum, cost or unit (null = keep; Gerente)")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [Authorize(Policy = "Gerente")]
    [HttpPut("insumos/{produtoId:guid}")]
    public async Task<IActionResult> AtualizarInsumo(Guid produtoId, [FromBody] InsumoRequest req, CancellationToken ct)
    {
        try
        {
            await insumos.AtualizarAsync(currentUser.EmpresaId, produtoId, new InsumoInput(null, req.Unidade, req.Minimo, req.Custo, req.Embalagem), ct);
            return NoContent();
        }
        catch (UseCaseValidationException ex)
        {
            return DataBadRequest(ex.Message);
        }
    }

    [SwaggerOperation(Summary = "Register today's production by menu dish: lot, labels and stock entry in portions (M2.2)",
        Description = "Envie Idempotency-Key: repetir com a mesma chave não cria lote novo. Prato avulso ganha produto de estoque.")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [HttpPost]
    public async Task<IActionResult> Produzir([FromBody] ProduzirPratosRequest req, CancellationToken ct)
    {
        if (!currentUser.TemPermissao(Permissao.GerenciarEstoque)) return Forbid();
        try
        {
            var r = await produzirPratos.ExecuteAsync(new ProduzirPratosCommand(
                currentUser.EmpresaId, currentUser.UsuarioId, null, req.Pratos, req.Observacao), ct);
            return DataCreated($"/api/lotes/{r.LoteId}", r);
        }
        catch (StorefrontNaoEncontradoException)
        {
            return DataNotFound("A empresa não tem vitrine ativa.");
        }
        catch (CardapioItemNaoEncontradoException)
        {
            return DataNotFound("Prato do cardápio não encontrado.");
        }
        catch (Exception ex) when (ex is UseCaseValidationException or RegraDeDominioVioladaException)
        {
            return DataBadRequest(ex.Message);
        }
    }

    [SwaggerOperation(Summary = "Today's stock by menu dish: portions, lots (expiring) and sold-without-stock alerts (M2.1)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [HttpGet("estoque-do-dia")]
    public async Task<IActionResult> EstoqueDoDia(CancellationToken ct)
    {
        if (!currentUser.TemPermissao(Permissao.GerenciarEstoque)) return Forbid();
        try
        {
            return DataOk(await estoqueDoDia.ExecuteAsync(currentUser.EmpresaId, ct));
        }
        catch (StorefrontNaoEncontradoException)
        {
            return DataNotFound("A empresa não tem vitrine ativa.");
        }
    }
}

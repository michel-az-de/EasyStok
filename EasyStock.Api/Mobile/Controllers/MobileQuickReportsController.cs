using EasyStock.Application.UseCases.QuickReports;
using EasyStock.Infra.Async.Reporting.QuickReports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Api.Mobile.Controllers;

/// <summary>
/// Quick Reports mobile — relatórios síncronos leves para o painel PWA/MAUI.
/// Sem motor assíncrono, sem fila, sem ReportRun persistido.
/// Requer autenticação JWT Bearer (mesmo esquema das outras rotas web).
///
/// Endpoints:
///   GET /api/mobile/reports/quick/vendas-hoje
///   GET /api/mobile/reports/quick/caixa-turno
///   GET /api/mobile/reports/quick/estoque-busca
///   GET /api/mobile/reports/quick/nfce-hoje
///   GET /api/mobile/reports/quick/vendas-vendedor-turno
/// </summary>
[ApiController]
[Route("api/mobile/reports/quick")]
[Authorize]
public sealed class MobileQuickReportsController(
    GetVendasHojeQuery vendasHoje,
    GetCaixaTurnoQuery caixaTurno,
    GetEstoqueBuscaQuery estoqueBusca,
    GetNfceHojeQuery nfceHoje,
    GetVendasVendedorTurnoQuery vendasVendedor,
    ILogger<MobileQuickReportsController> log) : ControllerBase
{
    // ─────────────────────────────────────────────────────────
    // GET /api/mobile/reports/quick/vendas-hoje
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// Resumo de vendas do dia atual.
    /// Retorna total, quantidade de vendas, ticket médio e top-5 produtos.
    /// Timeout soft: 1 s (query indexada).
    /// </summary>
    [HttpGet("vendas-hoje")]
    [ProducesResponseType(typeof(VendasHojeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> VendasHoje(
        [FromQuery] Guid? lojaId,
        CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5)); // hard cap 5s

        try
        {
            var dto = await vendasHoje.ExecuteAsync(lojaId, cts.Token);
            return Ok(new VendasHojeResponse(dto));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            log.LogWarning("quick/vendas-hoje excedeu o timeout de 5s");
            return StatusCode(StatusCodes.Status504GatewayTimeout, new
            {
                error = "O resumo de vendas demorou mais que o esperado. Tente novamente.",
                quickKey = "vendas-hoje",
            });
        }
    }

    // ─────────────────────────────────────────────────────────
    // GET /api/mobile/reports/quick/caixa-turno
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// Resumo do caixa do turno (dia atual).
    /// Retorna entradas, saídas, vendas e saldo atual.
    /// Timeout soft: 1 s (query indexada).
    /// </summary>
    [HttpGet("caixa-turno")]
    [ProducesResponseType(typeof(CaixaTurnoResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CaixaTurno(
        [FromQuery] Guid? lojaId,
        CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            var dto = await caixaTurno.ExecuteAsync(lojaId, cts.Token);
            return Ok(new CaixaTurnoResponse(dto));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            log.LogWarning("quick/caixa-turno excedeu o timeout de 5s");
            return StatusCode(StatusCodes.Status504GatewayTimeout, new
            {
                error = "O resumo de caixa demorou mais que o esperado. Tente novamente.",
                quickKey = "caixa-turno",
            });
        }
    }

    // ─────────────────────────────────────────────────────────
    // GET /api/mobile/reports/quick/estoque-busca?busca=xxx
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// Localiza um produto por SKU, código interno ou fragmento de nome
    /// e retorna a posição de estoque atual.
    /// Retorna o item mais relevante (maior quantidade atual) ou 204 se não encontrado.
    /// </summary>
    [HttpGet("estoque-busca")]
    [ProducesResponseType(typeof(EstoqueBuscaResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> EstoqueBusca(
        [FromQuery] string busca,
        [FromQuery] Guid? lojaId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(busca))
            return BadRequest(new { error = "Informe um termo de busca." });

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            var dto = await estoqueBusca.ExecuteAsync(busca, lojaId, cts.Token);
            if (dto is null) return NoContent();
            return Ok(new EstoqueBuscaResponse(dto));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            log.LogWarning("quick/estoque-busca excedeu o timeout de 5s. Busca={Busca}", busca);
            return StatusCode(StatusCodes.Status504GatewayTimeout, new
            {
                error = "A busca de estoque demorou mais que o esperado. Tente novamente.",
                quickKey = "estoque-busca",
            });
        }
    }

    // ─────────────────────────────────────────────────────────
    // GET /api/mobile/reports/quick/nfce-hoje
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// Resumo das NFC-e emitidas no dia atual.
    /// Retorna contagens por status e taxa de sucesso (Autorizadas / Finalizadas).
    /// </summary>
    [HttpGet("nfce-hoje")]
    [ProducesResponseType(typeof(NfceHojeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> NfceHoje(
        [FromQuery] Guid? lojaId,
        CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            var dto = await nfceHoje.ExecuteAsync(lojaId, cts.Token);
            return Ok(new NfceHojeResponse(dto));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            log.LogWarning("quick/nfce-hoje excedeu o timeout de 5s");
            return StatusCode(StatusCodes.Status504GatewayTimeout, new
            {
                error = "O resumo de NFC-e demorou mais que o esperado. Tente novamente.",
                quickKey = "nfce-hoje",
            });
        }
    }

    // ─────────────────────────────────────────────────────────
    // GET /api/mobile/reports/quick/vendas-vendedor-turno
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// Ranking de vendas por vendedor no dia atual.
    /// Útil para o app do vendedor ("como estou hoje?") e para o gerente de loja.
    /// </summary>
    [HttpGet("vendas-vendedor-turno")]
    [ProducesResponseType(typeof(VendasVendedorTurnoResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> VendasVendedorTurno(
        [FromQuery] Guid? lojaId,
        CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            var dto = await vendasVendedor.ExecuteAsync(lojaId, cts.Token);
            return Ok(new VendasVendedorTurnoResponse(dto));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            log.LogWarning("quick/vendas-vendedor-turno excedeu o timeout de 5s");
            return StatusCode(StatusCodes.Status504GatewayTimeout, new
            {
                error = "O ranking de vendedores demorou mais que o esperado. Tente novamente.",
                quickKey = "vendas-vendedor-turno",
            });
        }
    }

    // ─── Response wrappers ──────────────────────────────────

    public sealed record VendasHojeResponse(
        decimal Total,
        int QtdVendas,
        decimal TicketMedio,
        IReadOnlyList<TopProdutoResponse> TopProdutos)
    {
        public VendasHojeResponse(VendasHojeDto dto) : this(
            dto.Total,
            dto.QtdVendas,
            dto.TicketMedio,
            dto.TopProdutos.Select(p => new TopProdutoResponse(p.ProdutoId, p.Nome, p.Qtd)).ToList())
        { }
    }

    public sealed record TopProdutoResponse(Guid ProdutoId, string Nome, decimal Qtd);

    public sealed record CaixaTurnoResponse(
        decimal TotalEntradas,
        decimal TotalSaidas,
        decimal TotalVendas,
        decimal SaldoAtual,
        string? Operador)
    {
        public CaixaTurnoResponse(CaixaTurnoDto dto) : this(
            dto.TotalEntradas,
            dto.TotalSaidas,
            dto.TotalVendas,
            dto.SaldoAtual,
            dto.Operador)
        { }
    }

    public sealed record EstoqueBuscaResponse(
        Guid ItemEstoqueId,
        string Sku,
        string Nome,
        string? Variacao,
        string? LojaNome,
        decimal QtdAtual,
        decimal CustoUnitario,
        decimal ValorEstoque,
        string StatusEstoque)
    {
        public EstoqueBuscaResponse(EstoqueBuscaDto dto) : this(
            dto.ItemEstoqueId,
            dto.Sku,
            dto.Nome,
            dto.Variacao,
            dto.LojaNome,
            dto.QtdAtual,
            dto.CustoUnitario,
            dto.ValorEstoque,
            dto.StatusEstoque)
        { }
    }

    public sealed record NfceHojeResponse(
        int Autorizadas,
        int Canceladas,
        int Rejeitadas,
        int Pendentes,
        decimal PercentSucesso)
    {
        public NfceHojeResponse(NfceHojeDto dto) : this(
            dto.Autorizadas,
            dto.Canceladas,
            dto.Rejeitadas,
            dto.Pendentes,
            dto.PercentSucesso)
        { }
    }

    public sealed record VendasVendedorTurnoResponse(
        IReadOnlyList<VendedorTurnoItemResponse> Vendedores,
        decimal TotalGeral,
        int QtdVendasGeral)
    {
        public VendasVendedorTurnoResponse(VendasVendedorTurnoDto dto) : this(
            dto.Vendedores.Select(v => new VendedorTurnoItemResponse(
                v.VendedorId, v.VendedorNome, v.QtdVendas, v.TotalVendido, v.Ranking)).ToList(),
            dto.TotalGeral,
            dto.QtdVendasGeral)
        { }
    }

    public sealed record VendedorTurnoItemResponse(
        Guid? VendedorId,
        string VendedorNome,
        int QtdVendas,
        decimal TotalVendido,
        int Ranking);
}

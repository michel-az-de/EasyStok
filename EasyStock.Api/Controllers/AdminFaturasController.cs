using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.Faturas.CancelarFatura;
using EasyStock.Application.UseCases.Faturas.Common;
using EasyStock.Application.UseCases.Faturas.EmitirFatura;
using EasyStock.Application.UseCases.Faturas.ExportarFaturasCsv;
using EasyStock.Application.UseCases.Faturas.ListarFaturasAdmin;
using EasyStock.Application.UseCases.Faturas.MetricasFinanceiras;
using EasyStock.Application.UseCases.Faturas.ObterFaturaDetalhe;
using EasyStock.Application.UseCases.Faturas.RegistrarPagamentoFatura;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

/// <summary>
/// Endpoints administrativos do modulo Financeiro. SuperAdmin/Admin operacional
/// pode gerenciar todas as faturas (sujeito as permissoes finas
/// <see cref="Permissao.GerenciarFaturas"/>, <see cref="Permissao.EmitirFatura"/>,
/// <see cref="Permissao.CancelarFatura"/>).
/// </summary>
[SwaggerTag("Faturas (admin)")]
[Authorize]
[ApiController]
[Route("api/admin/faturas")]
public class AdminFaturasController(
    ListarFaturasAdminUseCase listarUseCase,
    ObterFaturaDetalheUseCase detalheUseCase,
    EmitirFaturaUseCase emitirUseCase,
    RegistrarPagamentoFaturaUseCase pagamentoUseCase,
    CancelarFaturaUseCase cancelarUseCase,
    ExportarFaturasCsvUseCase exportarCsvUseCase,
    MetricasFinanceirasUseCase metricasUseCase,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "Listar faturas (admin) com filtros amplos")]
    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] Guid? empresaId,
        [FromQuery] StatusFatura? status,
        [FromQuery] OrigemFatura? origem,
        [FromQuery] DateTime? vencimentoDe,
        [FromQuery] DateTime? vencimentoAte,
        [FromQuery] decimal? valorMin,
        [FromQuery] decimal? valorMax,
        [FromQuery] string? busca,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (!RequerPermissao(Permissao.VisualizarFaturas, out var err)) return err!;

        // Admin operacional (nao SuperAdmin) vê apenas sua empresa
        var efetivoEmpresaId = currentUser.Nivel == NivelAcesso.SuperAdmin
            ? empresaId
            : currentUser.EmpresaId;

        var result = await listarUseCase.ExecuteAsync(
            new ListarFaturasAdminCommand(
                efetivoEmpresaId,
                status, origem,
                vencimentoDe, vencimentoAte,
                valorMin, valorMax,
                busca, page, pageSize),
            ct);

        return DataPaged(result.Itens, result.Total, result.Page, result.PageSize);
    }

    [SwaggerOperation(Summary = "Obter detalhe de uma fatura (admin)")]
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Detalhe(Guid id, CancellationToken ct = default)
    {
        if (!RequerPermissao(Permissao.VisualizarFaturas, out var err)) return err!;

        var result = await detalheUseCase.ExecuteAsync(
            new ObterFaturaDetalheCommand(EmpresaId: null, FaturaId: id, Admin: true),
            ct);

        if (result is null) return DataNotFound("Fatura nao encontrada.");

        // Admin operacional só pode ver da sua empresa
        if (currentUser.Nivel != NivelAcesso.SuperAdmin
            && result.EmpresaId != currentUser.EmpresaId)
            return DataNotFound("Fatura nao encontrada.");

        return DataOk(result);
    }

    public sealed record EmitirAvulsaRequest(
        Guid EmpresaId,
        Guid? ClienteId,
        DadosFaturado DadosFaturado,
        DadosEmissor DadosEmissor,
        DateTime DataVencimento,
        IReadOnlyList<FaturaItemInput> Itens,
        string? Observacoes,
        DadosFiscais? DadosFiscais
    );

    [SwaggerOperation(Summary = "Emitir fatura avulsa")]
    [HttpPost("emitir")]
    public async Task<IActionResult> Emitir([FromBody] EmitirAvulsaRequest req, CancellationToken ct = default)
    {
        if (!RequerPermissao(Permissao.EmitirFatura, out var err)) return err!;

        // Admin operacional só emite para sua empresa
        var empresaIdEfetivo = currentUser.Nivel == NivelAcesso.SuperAdmin
            ? req.EmpresaId
            : currentUser.EmpresaId;

        try
        {
            var result = await emitirUseCase.ExecuteAsync(
                new EmitirFaturaCommand(
                    EmpresaId: empresaIdEfetivo,
                    DadosFaturado: req.DadosFaturado,
                    DadosEmissor: req.DadosEmissor,
                    Origem: OrigemFatura.Avulsa,
                    DataVencimento: req.DataVencimento,
                    Itens: req.Itens,
                    ClienteId: req.ClienteId,
                    Observacoes: req.Observacoes,
                    DadosFiscais: req.DadosFiscais,
                    UsuarioId: currentUser.UsuarioId,
                    OrigemRegistro: "admin"
                ),
                ct);
            return DataCreated($"/api/admin/faturas/{result.FaturaId}", result);
        }
        catch (UseCaseValidationException ex)
        {
            return DataBadRequest(ex.Message);
        }
    }

    public sealed record RegistrarPagamentoRequest(
        decimal Valor,
        string Metodo = "manual",
        string GatewayProvedor = "Manual",
        string? GatewayTransactionId = null,
        string? DadosGatewayJson = null,
        string? Observacao = null,
        bool Pendente = false
    );

    [SwaggerOperation(Summary = "Registrar pagamento manual em uma fatura")]
    [HttpPost("{id:guid}/pagamentos")]
    public async Task<IActionResult> RegistrarPagamento(
        Guid id,
        [FromBody] RegistrarPagamentoRequest req,
        CancellationToken ct = default)
    {
        if (!RequerPermissao(Permissao.GerenciarFaturas, out var err)) return err!;

        var fatura = await detalheUseCase.ExecuteAsync(
            new ObterFaturaDetalheCommand(EmpresaId: null, FaturaId: id, Admin: true),
            ct);
        if (fatura is null) return DataNotFound("Fatura nao encontrada.");
        if (currentUser.Nivel != NivelAcesso.SuperAdmin
            && fatura.EmpresaId != currentUser.EmpresaId)
            return DataNotFound("Fatura nao encontrada.");

        try
        {
            var result = await pagamentoUseCase.ExecuteAsync(
                new RegistrarPagamentoFaturaCommand(
                    EmpresaId: fatura.EmpresaId,
                    FaturaId: id,
                    Metodo: req.Metodo,
                    Valor: req.Valor,
                    GatewayProvedor: req.GatewayProvedor,
                    GatewayTransactionId: req.GatewayTransactionId,
                    DadosGatewayJson: req.DadosGatewayJson,
                    StatusInicial: req.Pendente ? StatusFaturaPagamento.Pendente : StatusFaturaPagamento.Confirmado,
                    RegistradoPorUserId: currentUser.UsuarioId,
                    Observacao: req.Observacao,
                    OrigemRegistro: "admin"
                ),
                ct);
            return DataOk(result);
        }
        catch (UseCaseValidationException ex)
        {
            return DataBadRequest(ex.Message);
        }
    }

    public sealed record CancelarRequest(string? Motivo);

    [SwaggerOperation(Summary = "Cancelar fatura")]
    [HttpPost("{id:guid}/cancelar")]
    public async Task<IActionResult> Cancelar(
        Guid id,
        [FromBody] CancelarRequest? body,
        CancellationToken ct = default)
    {
        if (!RequerPermissao(Permissao.CancelarFatura, out var err)) return err!;

        var fatura = await detalheUseCase.ExecuteAsync(
            new ObterFaturaDetalheCommand(EmpresaId: null, FaturaId: id, Admin: true),
            ct);
        if (fatura is null) return DataNotFound("Fatura nao encontrada.");
        if (currentUser.Nivel != NivelAcesso.SuperAdmin
            && fatura.EmpresaId != currentUser.EmpresaId)
            return DataNotFound("Fatura nao encontrada.");

        try
        {
            await cancelarUseCase.ExecuteAsync(
                new CancelarFaturaCommand(
                    EmpresaId: fatura.EmpresaId,
                    FaturaId: id,
                    Motivo: body?.Motivo,
                    UsuarioId: currentUser.UsuarioId,
                    OrigemRegistro: "admin"
                ),
                ct);
            return DataOk(new { message = "Fatura cancelada." });
        }
        catch (UseCaseValidationException ex)
        {
            return DataBadRequest(ex.Message);
        }
    }

    [SwaggerOperation(Summary = "Exportar faturas filtradas como CSV (UTF-8 BOM, separador ;)")]
    [HttpGet("export.csv")]
    public async Task<IActionResult> ExportarCsv(
        [FromQuery] Guid? empresaId,
        [FromQuery] StatusFatura? status,
        [FromQuery] OrigemFatura? origem,
        [FromQuery] DateTime? vencimentoDe,
        [FromQuery] DateTime? vencimentoAte,
        [FromQuery] decimal? valorMin,
        [FromQuery] decimal? valorMax,
        [FromQuery] string? busca,
        CancellationToken ct = default)
    {
        if (!RequerPermissao(Permissao.VisualizarFaturas, out var err)) return err!;

        // Admin operacional: forca empresa do user (igual a Listar).
        var efetivoEmpresaId = currentUser.Nivel == NivelAcesso.SuperAdmin
            ? empresaId
            : currentUser.EmpresaId;

        var bytes = await exportarCsvUseCase.ExecuteAsync(
            new ExportarFaturasCsvCommand(
                efetivoEmpresaId, status, origem,
                vencimentoDe, vencimentoAte,
                valorMin, valorMax, busca),
            ct);

        var ts = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        return File(bytes, "text/csv; charset=utf-8", $"faturas-{ts}.csv");
    }

    [SwaggerOperation(Summary = "Metricas financeiras (MRR, ARR, churn, atraso, top inadimplentes)")]
    [HttpGet("metricas")]
    public async Task<IActionResult> Metricas(
        [FromQuery] int dias = 30,
        [FromQuery] Guid? empresaId = null,
        [FromQuery] bool forcarRefresh = false,
        CancellationToken ct = default)
    {
        if (!RequerPermissao(Permissao.VisualizarFaturas, out var err)) return err!;

        var efetivoEmpresaId = currentUser.Nivel == NivelAcesso.SuperAdmin
            ? empresaId
            : currentUser.EmpresaId;

        var result = await metricasUseCase.ExecuteAsync(
            new MetricasFinanceirasCommand(dias, efetivoEmpresaId, forcarRefresh),
            ct);

        return DataOk(result);
    }

    private bool RequerPermissao(Permissao permissao, out IActionResult? error)
    {
        if (!currentUser.TemPermissao(permissao))
        {
            error = Forbid();
            return false;
        }
        error = null;
        return true;
    }
}

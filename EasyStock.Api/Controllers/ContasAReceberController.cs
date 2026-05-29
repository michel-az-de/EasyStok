using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.Financeiro.ContasReceber;
using EasyStock.Application.UseCases.Financeiro.Pagamentos;
using EasyStock.Domain.Enums.Financeiro;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

[SwaggerTag("Contas a Receber")]
[Authorize]
[ValidateEmpresaId]
[ApiController]
[Route("api/contas-a-receber")]
public class ContasAReceberController(
    CriarContaReceberUseCase criarUseCase,
    AtualizarContaReceberUseCase atualizarUseCase,
    EmitirContaReceberUseCase emitirUseCase,
    CancelarContaReceberUseCase cancelarUseCase,
    AdicionarParcelaContaReceberUseCase adicionarParcelaUseCase,
    RemoverParcelaContaReceberUseCase removerParcelaUseCase,
    ListarContasReceberUseCase listarUseCase,
    ObterContaReceberDetalheUseCase detalheUseCase,
    RegistrarPagamentoParcelaReceberUseCase registrarPagamentoUseCase,
    EstornarPagamentoParcelaReceberUseCase estornarPagamentoUseCase,
    GerarPixQrParcelaReceberUseCase gerarPixUseCase,
    LimparPixParcelaReceberUseCase limparPixUseCase,
    ReconciliarPixParcelaReceberUseCase reconciliarPixUseCase,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] Guid? empresaId,
        [FromQuery] StatusContaFinanceira? status,
        [FromQuery] Guid? clienteId,
        [FromQuery] Guid? categoriaId,
        [FromQuery] Guid? centroCustoId,
        [FromQuery] DateTime? vencimentoDe,
        [FromQuery] DateTime? vencimentoAte,
        [FromQuery] string? busca,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? sort = "datavencimento",
        [FromQuery] string? order = "asc",
        CancellationToken ct = default)
    {
        if (!RequerVisualizar(out var permErr)) return permErr!;
        if (!TryResolveEmpresaId(currentUser, empresaId, out var eid, out var err)) return err!;
        var (p, sz) = NormalisePage(page, pageSize);
        var r = await listarUseCase.ExecuteAsync(new ListarContasReceberQuery(
            eid, status, clienteId, categoriaId, centroCustoId,
            vencimentoDe, vencimentoAte, busca, p, sz, sort, NormaliseOrder(order)), ct);
        return DataPaged(r.Itens, r.Total, r.Page, r.PageSize);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Detalhe(Guid id, [FromQuery] Guid? empresaId, CancellationToken ct = default)
    {
        if (!RequerVisualizar(out var permErr)) return permErr!;
        if (!TryResolveEmpresaId(currentUser, empresaId, out var eid, out var err)) return err!;
        var r = await detalheUseCase.ExecuteAsync(new ObterContaReceberDetalheQuery(eid, id), ct);
        return r is null ? DataNotFound("Conta a receber nao encontrada.") : DataOk(r);
    }

    [HttpPost]
    public async Task<IActionResult> Criar([FromBody] CriarContaReceberCommand cmd, CancellationToken ct = default)
    {
        if (!RequerGerenciar(out var permErr)) return permErr!;
        if (!TryResolveEmpresaId(currentUser, cmd.EmpresaId, out var eid, out var err)) return err!;
        try
        {
            var r = await criarUseCase.ExecuteAsync(cmd with { EmpresaId = eid }, ct);
            return DataCreated($"/api/contas-a-receber/{r.Id}", r);
        }
        catch (UseCaseValidationException ex) { return DataBadRequest(ex.Message); }
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Atualizar(Guid id, [FromBody] AtualizarContaReceberCommand cmd, CancellationToken ct = default)
    {
        if (!RequerGerenciar(out var permErr)) return permErr!;
        if (!TryResolveEmpresaId(currentUser, cmd.EmpresaId, out var eid, out var err)) return err!;
        if (id != cmd.Id) return DataBadRequest("Id da rota difere do corpo.");
        try
        {
            var r = await atualizarUseCase.ExecuteAsync(cmd with { EmpresaId = eid, Id = id }, ct);
            return r is null ? DataNotFound() : DataOk(r);
        }
        catch (UseCaseValidationException ex) { return DataBadRequest(ex.Message); }
    }

    [HttpPost("{id:guid}/emitir")]
    public async Task<IActionResult> Emitir(Guid id, [FromQuery] Guid? empresaId, CancellationToken ct = default)
    {
        if (!RequerGerenciar(out var permErr)) return permErr!;
        if (!TryResolveEmpresaId(currentUser, empresaId, out var eid, out var err)) return err!;
        try
        {
            var r = await emitirUseCase.ExecuteAsync(new EmitirContaReceberCommand(
                eid, id, currentUser.UsuarioId == Guid.Empty ? null : currentUser.UsuarioId, null), ct);
            return r is null ? DataNotFound() : DataOk(r);
        }
        catch (UseCaseValidationException ex) { return DataBadRequest(ex.Message); }
    }

    [HttpPost("{id:guid}/cancelar")]
    public async Task<IActionResult> Cancelar(Guid id, [FromBody] CancelarContaRequest body, CancellationToken ct = default)
    {
        if (!RequerGerenciar(out var permErr)) return permErr!;
        if (!TryResolveEmpresaId(currentUser, body.EmpresaId, out var eid, out var err)) return err!;
        try
        {
            var r = await cancelarUseCase.ExecuteAsync(new CancelarContaReceberCommand(
                eid, id, body.Motivo,
                currentUser.UsuarioId == Guid.Empty ? null : currentUser.UsuarioId, null), ct);
            return r is null ? DataNotFound() : DataOk(r);
        }
        catch (UseCaseValidationException ex) { return DataBadRequest(ex.Message); }
    }

    [HttpPost("{id:guid}/parcelas")]
    public async Task<IActionResult> AdicionarParcela(Guid id, [FromBody] AdicionarParcelaContaReceberCommand cmd, CancellationToken ct = default)
    {
        if (!RequerGerenciar(out var permErr)) return permErr!;
        if (!TryResolveEmpresaId(currentUser, cmd.EmpresaId, out var eid, out var err)) return err!;
        if (id != cmd.ContaReceberId) return DataBadRequest("Id da rota difere do corpo.");
        try
        {
            var r = await adicionarParcelaUseCase.ExecuteAsync(cmd with { EmpresaId = eid, ContaReceberId = id }, ct);
            return r is null ? DataNotFound() : DataOk(r);
        }
        catch (UseCaseValidationException ex) { return DataBadRequest(ex.Message); }
    }

    [HttpDelete("{id:guid}/parcelas/{parcelaId:guid}")]
    public async Task<IActionResult> RemoverParcela(Guid id, Guid parcelaId, [FromQuery] Guid? empresaId, CancellationToken ct = default)
    {
        if (!RequerGerenciar(out var permErr)) return permErr!;
        if (!TryResolveEmpresaId(currentUser, empresaId, out var eid, out var err)) return err!;
        try
        {
            var r = await removerParcelaUseCase.ExecuteAsync(new RemoverParcelaContaReceberCommand(eid, id, parcelaId), ct);
            return r is null ? DataNotFound() : DataOk(r);
        }
        catch (UseCaseValidationException ex) { return DataBadRequest(ex.Message); }
    }

    [HttpPost("{id:guid}/parcelas/{parcelaId:guid}/pagamentos")]
    public async Task<IActionResult> RegistrarPagamento(
        Guid id, Guid parcelaId,
        [FromBody] RegistrarPagamentoBody body,
        CancellationToken ct = default)
    {
        if (!RequerVisualizar(out var permErr)) return permErr!;
        if (!TryResolveEmpresaId(currentUser, body.EmpresaId, out var eid, out var err)) return err!;
        try
        {
            var r = await registrarPagamentoUseCase.ExecuteAsync(new RegistrarPagamentoParcelaReceberCommand(
                eid, parcelaId, body.Valor, body.Metodo, body.DataPagamento,
                body.Observacao, body.GatewayProvedor ?? "Manual", body.GatewayTransactionId,
                currentUser.UsuarioId == Guid.Empty ? null : currentUser.UsuarioId, null), ct);
            return r is null ? DataNotFound() : DataOk(r);
        }
        catch (UseCaseValidationException ex) { return DataBadRequest(ex.Message); }
    }

    [HttpPost("parcelas/{parcelaId:guid}/pagamentos/{pagId:guid}/estornar")]
    public async Task<IActionResult> EstornarPagamento(
        Guid parcelaId, Guid pagId,
        [FromBody] EstornarPagamentoBody body,
        CancellationToken ct = default)
    {
        if (!RequerGerenciar(out var permErr)) return permErr!;
        if (!TryResolveEmpresaId(currentUser, body.EmpresaId, out var eid, out var err)) return err!;
        try
        {
            var ok = await estornarPagamentoUseCase.ExecuteAsync(new EstornarPagamentoParcelaReceberCommand(
                eid, parcelaId, pagId, body.Motivo,
                currentUser.UsuarioId == Guid.Empty ? null : currentUser.UsuarioId, null), ct);
            return ok ? NoContent() : DataNotFound();
        }
        catch (UseCaseValidationException ex) { return DataBadRequest(ex.Message); }
    }

    [HttpPost("parcelas/{parcelaId:guid}/pix")]
    public async Task<IActionResult> GerarPix(Guid parcelaId, [FromBody] GerarPixBody body, CancellationToken ct = default)
    {
        if (!RequerGerenciar(out var permErr)) return permErr!;
        if (!TryResolveEmpresaId(currentUser, body.EmpresaId, out var eid, out var err)) return err!;
        try
        {
            var r = await gerarPixUseCase.ExecuteAsync(new GerarPixQrParcelaReceberCommand(eid, parcelaId), ct);
            return r is null ? DataNotFound() : DataOk(r);
        }
        catch (UseCaseValidationException ex) { return DataBadRequest(ex.Message); }
    }

    [HttpDelete("parcelas/{parcelaId:guid}/pix")]
    public async Task<IActionResult> LimparPix(Guid parcelaId, [FromQuery] Guid? empresaId, CancellationToken ct = default)
    {
        if (!RequerGerenciar(out var permErr)) return permErr!;
        if (!TryResolveEmpresaId(currentUser, empresaId, out var eid, out var err)) return err!;
        var ok = await limparPixUseCase.ExecuteAsync(new LimparPixParcelaReceberCommand(eid, parcelaId), ct);
        return ok ? NoContent() : DataNotFound();
    }

    [HttpPost("parcelas/{parcelaId:guid}/reconciliar")]
    public async Task<IActionResult> ReconciliarManual(Guid parcelaId, [FromBody] ReconciliarBody body, CancellationToken ct = default)
    {
        if (!RequerGerenciar(out var permErr)) return permErr!;
        var r = await reconciliarPixUseCase.ExecuteAsync(new ReconciliarPixParcelaReceberCommand(
            body.Txid, body.ValorPagoEfi, body.PagoEm, null), ct);
        return DataOk(r);
    }

    public sealed record CancelarContaRequest(Guid EmpresaId, string Motivo);
    public sealed record RegistrarPagamentoBody(
        Guid EmpresaId,
        decimal Valor,
        string Metodo,
        DateTime? DataPagamento,
        string? Observacao,
        string? GatewayProvedor,
        string? GatewayTransactionId);
    public sealed record EstornarPagamentoBody(Guid EmpresaId, string? Motivo);
    public sealed record GerarPixBody(Guid EmpresaId);
    public sealed record ReconciliarBody(string Txid, decimal? ValorPagoEfi, DateTime? PagoEm);

    private bool RequerVisualizar(out IActionResult? error)
    {
        if (!currentUser.TemPermissao(Permissao.VisualizarContasAReceber)) { error = Forbid(); return false; }
        error = null; return true;
    }
    private bool RequerGerenciar(out IActionResult? error)
    {
        if (!currentUser.TemPermissao(Permissao.GerenciarContasAReceber)) { error = Forbid(); return false; }
        error = null; return true;
    }
}
